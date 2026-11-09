#include "chat.h"
#include "common.h"
#include "llama.h"
#include "log.h"
#include "mtmd.h"
#include "mtmd-helper.h"

#include <algorithm>
#include <cmath>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

#define RAMBL_API extern "C" __declspec(dllexport)

using cancel_callback = int (__cdecl *)();
struct cancelled {};

// Order matters: templates and context are destroyed before the model.
struct model_session {
    std::unique_ptr<llama_model, decltype(&llama_model_free)> model{nullptr, llama_model_free};
    std::unique_ptr<llama_context, decltype(&llama_free)> context{nullptr, llama_free};
    common_chat_templates_ptr templates;
    mtmd::context_ptr media;
    std::vector<float> audio;
    std::string result;
    std::string error;
};

static thread_local std::string load_error;
static std::once_flag initialized;

static void check_cancel(cancel_callback callback) {
    if (callback && callback()) throw cancelled{};
}

static bool abort_decode(void * data) {
    const auto callback = *static_cast<cancel_callback *>(data);
    return callback && callback();
}

static bool load_progress(float, void * data) {
    return !abort_decode(data);
}

// The managed boundary uses only pointers, UTF-8, and fixed-width integers.
// Upstream C++ structs stay here, compiled against the pinned source.
RAMBL_API int rambl_llama_abi_version() { return 2; }
RAMBL_API const char * rambl_llama_version() { return RAMBL_LLAMA_VERSION; }
RAMBL_API const char * rambl_llama_load_error() { return load_error.c_str(); }

RAMBL_API model_session * rambl_llama_load(const char * path, const char * projector_path, int context_size, int threads, cancel_callback cancel) {
    try {
        load_error.clear();
        if (!path || context_size < 1 || threads < 1) throw std::invalid_argument("Invalid model load options.");
        check_cancel(cancel);
        std::call_once(initialized, [] {
            common_log_set_verbosity_thold(LOG_LEVEL_WARN);
            llama_log_set(common_log_default_callback, nullptr);
            mtmd_helper_log_set(common_log_default_callback, nullptr);
            llama_backend_init();
        });
        auto session = std::make_unique<model_session>();
        auto parameters = llama_model_default_params();
        parameters.n_gpu_layers = 0;
        parameters.progress_callback = load_progress;
        parameters.progress_callback_user_data = &cancel;
        session->model.reset(llama_model_load_from_file(path, parameters));
        check_cancel(cancel);
        if (!session->model) throw std::runtime_error("The language model could not be loaded. Check its files and available memory.");
        auto ctx = llama_context_default_params();
        ctx.n_ctx = context_size;
        ctx.n_batch = 512;
        ctx.n_ubatch = 512;
        ctx.n_seq_max = 1;
        ctx.n_threads = threads;
        ctx.n_threads_batch = threads;
        ctx.offload_kqv = false;
        ctx.op_offload = false;
        ctx.no_perf = true;
        ctx.embeddings = projector_path && *projector_path;
        ctx.pooling_type = LLAMA_POOLING_TYPE_NONE;
        session->context.reset(llama_init_from_model(session->model.get(), ctx));
        check_cancel(cancel);
        if (!session->context) throw std::runtime_error("There was not enough memory to create the language model context.");
        // Use upstream Jinja, rather than the C API's limited template list.
        session->templates = common_chat_templates_init(session->model.get(), "");
        if (projector_path && *projector_path) {
            auto media_params = mtmd_context_params_default();
            media_params.use_gpu = false;
            media_params.n_threads = threads;
            media_params.warmup = false;
            media_params.progress_callback = load_progress;
            media_params.progress_callback_user_data = &cancel;
            session->media.reset(mtmd_init_from_file(projector_path, session->model.get(), media_params));
            check_cancel(cancel);
            if (!session->media || !mtmd_support_audio(session->media.get()) ||
                mtmd_gen_audio_get_info(session->media.get()).type == MTMD_GEN_AUDIO_TYPE_NONE)
                throw std::runtime_error("The Omni audio components could not be loaded.");
        }
        return session.release();
    } catch (const cancelled &) {
        load_error = "The model load was cancelled.";
    } catch (const std::exception & ex) {
        load_error = ex.what();
    } catch (...) {
        load_error = "The native language runtime could not load the model.";
    }
    return nullptr;
}

static void decode(model_session & session, const llama_token * tokens, int count, int position, cancel_callback cancel);

static std::string generate_text(model_session & session, int position, int budget,
                                 const common_chat_params & chat, cancel_callback cancel) {
    auto ctx = session.context.get();
    const auto vocab = llama_model_get_vocab(session.model.get());
    std::unique_ptr<llama_sampler, decltype(&llama_sampler_free)> sampler(llama_sampler_init_greedy(), llama_sampler_free);
    common_chat_input generated;
    for (int i = 0; i < budget; ++i) {
        check_cancel(cancel);
        const auto token = llama_sampler_sample(sampler.get(), ctx, -1);
        if (llama_vocab_is_eog(vocab, token)) break;
        generated.append(common_token_to_piece(vocab, token, true), token);
        if (i + 1 == budget) break;
        decode(session, &token, 1, position++, cancel);
    }
    common_chat_parser_params parser(chat);
    parser.parser = chat.parser;
    return common_chat_parse(generated, false, parser).content;
}

struct request_scope {
    llama_context * ctx;
    request_scope(model_session & session, cancel_callback & cancel) : ctx(session.context.get()) {
        check_cancel(cancel);
        llama_memory_clear(llama_get_memory(ctx), true);
        llama_set_abort_callback(ctx, abort_decode, &cancel);
    }
    ~request_scope() { llama_set_abort_callback(ctx, nullptr, nullptr); }
};

RAMBL_API int rambl_llama_transcribe(model_session * session, const float * samples, int sample_count,
                                    const char * language, cancel_callback cancel, const char ** output) {
    if (!session || !output) return 1;
    *output = nullptr;
    try {
        if (!session->media || !samples || sample_count < 1) throw std::invalid_argument("Omni audio input is missing.");
        request_scope request(*session, cancel);
        common_chat_templates_inputs inputs;
        inputs.enable_thinking = false;
        common_chat_msg system;
        system.role = "system";
        system.content = language && *language ? std::string("Perform ASR in ") + language + "." : "Transcribe the speech. Respond with only the transcript.";
        common_chat_msg user;
        user.role = "user";
        user.content = mtmd_default_marker();
        inputs.messages = {system, user};
        const auto chat = common_chat_templates_apply(session->templates.get(), inputs);
        const auto prompt = chat.prompt.str();
        mtmd_input_text text{prompt.c_str(), prompt.size(), true, true};
        mtmd::bitmap_ptr bitmap(mtmd_bitmap_init_from_audio(sample_count, samples));
        mtmd::input_chunks_ptr chunks(mtmd_input_chunks_init());
        const mtmd_bitmap * bitmaps[] = {bitmap.get()};
        if (mtmd_tokenize(session->media.get(), chunks.get(), &text, bitmaps, 1) != 0)
            throw std::runtime_error("The recorded speech could not be prepared for Omni.");
        const auto count = mtmd_helper_get_n_pos(chunks.get());
        const auto context_size = llama_n_ctx(session->context.get());
        if (count < 1 || count + 32 >= context_size) throw std::runtime_error("This speech segment is too long for Omni.");
        llama_pos position = 0;
        const int status = mtmd_helper_eval_chunks(session->media.get(), session->context.get(), chunks.get(), 0, 0,
                                                  llama_n_batch(session->context.get()), true, &position);
        check_cancel(cancel);
        if (status != 0) throw std::runtime_error("Omni could not encode the recorded speech.");
        session->result = generate_text(*session, position, std::min<int>(2048, context_size - position), chat, cancel);
        check_cancel(cancel);
        *output = session->result.c_str();
        return 0;
    } catch (const cancelled &) { return 2; }
    catch (const std::exception & ex) { session->error = ex.what(); }
    catch (...) { session->error = "Omni could not transcribe the recording."; }
    *output = session->error.c_str();
    return 1;
}

RAMBL_API int rambl_llama_synthesize(model_session * session, const char * text, const char * voice, const char * language,
                                    cancel_callback cancel, const float ** samples, int * sample_count, int * sample_rate,
                                    const char ** error) {
    if (!session || !samples || !sample_count || !sample_rate || !error) return 1;
    *samples = nullptr; *sample_count = 0; *sample_rate = 0; *error = nullptr;
    try {
        if (!session->media || !text || !voice || !language) throw std::invalid_argument("Omni speech options are missing.");
        request_scope request(*session, cancel);
        const auto prompt = common_chat_get_tts_prompt(session->templates.get(), text, language, voice);
        if (prompt.empty()) throw std::runtime_error("This model has no supported speech template.");
        const auto vocab = llama_model_get_vocab(session->model.get());
        if (common_tokenize(vocab, prompt, true, true).size() + 512 >= llama_n_ctx(session->context.get()))
            throw std::runtime_error("The text is too long for one Omni speech segment.");
        mtmd_helper::gen_audio gen;
        gen.init(session->context.get(), session->media.get());
        auto input = mtmd_helper_gen_audio_inp_default();
        input.prompt = prompt.c_str(); input.prompt_len = prompt.size(); input.lang = language;
        input.out_type = MTMD_HELPER_GEN_AUDIO_OUTTYPE_PCM;
        if (gen.set_input(&input) != 0) throw std::runtime_error("Omni could not prepare the speech request.");
        for (;;) {
            check_cancel(cancel);
            const auto remaining = gen.step_prompt(llama_n_batch(session->context.get()));
            if (remaining < 0) throw std::runtime_error("Omni could not evaluate the speech prompt.");
            if (remaining == 0) break;
        }
        std::unique_ptr<llama_sampler, decltype(&llama_sampler_free)> sampler(llama_sampler_init_greedy(), llama_sampler_free);
        auto sampled = llama_sampler_sample(sampler.get(), session->context.get(), -1);
        const float * hidden = llama_get_embeddings_ith(session->context.get(), -1);
        bool stopped = false;
        // Bound runaway synthesis. A normal reading is split into sentences before reaching this API.
        for (int frame = 0; frame < 2048 && !stopped; ++frame) {
            check_cancel(cancel);
            const float * next = nullptr;
            if (gen.step_gen(sampled, hidden, &next, &stopped) != 0) {
                check_cancel(cancel);
                throw std::runtime_error("Omni could not generate speech.");
            }
            if (!next) { stopped = true; break; }
            hidden = next;
            sampled = llama_sampler_sample(sampler.get(), session->context.get(), -1);
        }
        check_cancel(cancel);
        if (!stopped) throw std::runtime_error("Omni speech exceeded its output limit. Use a shorter sentence.");
        const char * data = nullptr;
        size_t size = 0;
        int64_t count = 0;
        int32_t rate = 0;
        if (gen.get_output(&rate, &data, &size, &count) != 0 || !data || count <= 0 ||
            count > INT32_MAX || size != static_cast<size_t>(count) * sizeof(float))
            throw std::runtime_error("Omni returned invalid speech audio.");
        const auto pcm = reinterpret_cast<const float *>(data);
        session->audio.assign(pcm, pcm + count);
        check_cancel(cancel);
        *samples = session->audio.data(); *sample_count = static_cast<int>(count); *sample_rate = rate;
        return 0;
    } catch (const cancelled &) { return 2; }
    catch (const std::exception & ex) { session->error = ex.what(); }
    catch (...) { session->error = "Omni could not synthesize speech."; }
    *error = session->error.c_str();
    return 1;
}

RAMBL_API void rambl_llama_free(model_session * session) { delete session; }

static void decode(model_session & session, const llama_token * tokens, int count, int position, cancel_callback cancel) {
    check_cancel(cancel);
    auto batch = llama_batch_init(count, 0, 1);
    batch.n_tokens = count;
    for (int i = 0; i < count; ++i) {
        batch.token[i] = tokens[i];
        batch.pos[i] = position + i;
        batch.n_seq_id[i] = 1;
        batch.seq_id[i][0] = 0;
        batch.logits[i] = i == count - 1;
    }
    const int status = llama_decode(session.context.get(), batch);
    llama_batch_free(batch);
    check_cancel(cancel);
    if (status != 0) throw std::runtime_error("The language model could not evaluate the request.");
}

RAMBL_API int rambl_llama_complete(
    model_session * session, const char * const * roles, const char * const * contents, int count,
    int max_tokens, double temperature, cancel_callback cancel, const char ** output) {
    if (!session || !output) return 1;
    *output = nullptr;
    try {
        session->result.clear();
        session->error.clear();
        if (!roles || !contents || count < 1 || max_tokens < 1 || !std::isfinite(temperature) || temperature < 0)
            throw std::invalid_argument("Invalid language generation options.");
        check_cancel(cancel);
        common_chat_templates_inputs inputs;
        inputs.enable_thinking = false;
        inputs.reasoning_format = COMMON_REASONING_FORMAT_NONE;
        for (int i = 0; i < count; ++i) {
            if (!roles[i] || !contents[i]) throw std::invalid_argument("A chat message is missing.");
            common_chat_msg message;
            message.role = roles[i];
            message.content = contents[i];
            inputs.messages.push_back(std::move(message));
        }
        const auto chat = common_chat_templates_apply(session->templates.get(), inputs);
        const auto vocab = llama_model_get_vocab(session->model.get());
        const auto tokens = common_tokenize(vocab, chat.prompt.str(), true, true);
        const auto context_size = llama_n_ctx(session->context.get());
        if (tokens.empty() || tokens.size() >= context_size)
            throw std::runtime_error("The text is too long for the language model's context. Use a shorter recording or process step.");
        const int budget = std::min<size_t>(max_tokens, context_size - tokens.size());
        auto ctx = session->context.get();
        llama_memory_clear(llama_get_memory(ctx), true);
        llama_set_abort_callback(ctx, abort_decode, &cancel);
        // Always remove the callback before its request-local state goes out of scope.
        struct abort_reset {
            llama_context * ctx;
            ~abort_reset() { llama_set_abort_callback(ctx, nullptr, nullptr); }
        } reset{ctx};
        int position = 0;
        while (position < static_cast<int>(tokens.size())) {
            const int size = std::min<int>(llama_n_batch(ctx), tokens.size() - position);
            decode(*session, tokens.data() + position, size, position, cancel);
            position += size;
        }
        auto sampler_params = llama_sampler_chain_default_params();
        std::unique_ptr<llama_sampler, decltype(&llama_sampler_free)> sampler(
            llama_sampler_chain_init(sampler_params), llama_sampler_free);
        if (temperature == 0) {
            llama_sampler_chain_add(sampler.get(), llama_sampler_init_greedy());
        } else {
            llama_sampler_chain_add(sampler.get(), llama_sampler_init_top_k(40));
            llama_sampler_chain_add(sampler.get(), llama_sampler_init_top_p(0.95f, 1));
            llama_sampler_chain_add(sampler.get(), llama_sampler_init_temp(static_cast<float>(temperature)));
            llama_sampler_chain_add(sampler.get(), llama_sampler_init_dist(LLAMA_DEFAULT_SEED));
        }
        common_chat_input generated;
        for (int i = 0; i < budget; ++i) {
            check_cancel(cancel);
            const auto token = llama_sampler_sample(sampler.get(), ctx, -1);
            if (llama_vocab_is_eog(vocab, token)) break;
            generated.append(common_token_to_piece(vocab, token, true), token);
            bool stopped = false;
            for (const auto & stop : chat.additional_stops) {
                if (!stop.empty() && generated.text.find(stop) != std::string::npos) {
                    generated.truncate(generated.text.find(stop));
                    stopped = true;
                    break;
                }
            }
            if (stopped || i + 1 == budget) break;
            decode(*session, &token, 1, position++, cancel);
        }
        common_chat_parser_params parser(chat);
        parser.parser = chat.parser;
        session->result = common_chat_parse(generated, false, parser).content;
        check_cancel(cancel);
        *output = session->result.c_str();
        return 0;
    } catch (const cancelled &) {
        return 2;
    } catch (const std::exception & ex) {
        session->error = ex.what();
    } catch (...) {
        session->error = "The native language runtime could not answer.";
    }
    *output = session->error.c_str();
    return 1;
}
