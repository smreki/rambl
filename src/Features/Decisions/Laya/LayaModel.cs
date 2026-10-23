using Microsoft.ML.OnnxRuntime;
using Rambl.Features.Decisions.Tokenizers;

namespace Rambl.Features.Decisions.Laya;

/// <summary>A loaded Laya checkpoint: one ONNX Runtime session with its tokenizer.</summary>
/// <remarks>
/// One forward pass scores every option at its marker; the scores are divided by the checkpoint's temperature for that
/// many options and turned into probabilities, and the confidence is one minus their normalized entropy, as in Laya's
/// reference code. Questions are answered one at a time.
/// </remarks>
public sealed class LayaModel : IDecisionModel
{
    private const int ChoiceQuestionType = 0;

    private readonly InferenceSession session;
    private readonly BpeTokenizer tokenizer;
    private readonly LayaSpecialTokens tokens;
    private readonly LayaSettings settings;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    private LayaModel(InferenceSession session, BpeTokenizer tokenizer, LayaSpecialTokens tokens, LayaSettings settings)
    {
        this.session = session;
        this.tokenizer = tokenizer;
        this.tokens = tokens;
        this.settings = settings;
    }

    /// <summary>Loads a checkpoint from its folder.</summary>
    /// <param name="directory">The folder with <c>config.json</c>, <c>tokenizer.json</c> and <c>tokenizer_config.json</c>.</param>
    /// <param name="modelFile">The ONNX file, relative to <paramref name="directory"/>; its external data file lies beside it.</param>
    /// <param name="threads">The threads one question may use.</param>
    /// <returns>The loaded model.</returns>
    /// <exception cref="DecisionModelException">A file is missing or not what a Laya checkpoint has.</exception>
    public static LayaModel Load(string directory, string modelFile, int threads)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(modelFile);

        BpeTokenizer tokenizer;
        try
        {
            tokenizer = BpeTokenizer.Load(Path.Combine(directory, "tokenizer.json"));
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException)
        {
            throw new DecisionModelException("The model's tokenizer could not be read.", ex);
        }

        var tokens = LayaSpecialTokens.Load(Path.Combine(directory, "tokenizer_config.json"), tokenizer);
        var settings = LayaSettings.Load(Path.Combine(directory, "config.json"));

        using var options = new SessionOptions
        {
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        var session = new InferenceSession(Path.Combine(directory, modelFile), options);
        return new(session, tokenizer, tokens, settings);
    }

    /// <inheritdoc/>
    public async Task<DecisionResult> ChooseAsync(DecisionQuestion question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.Options.Count == 0)
            throw new DecisionModelException("There is nothing to choose from.");

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return await Task.Run(() => Choose(question, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        // A question still running holds the gate; the session goes once it is done.
        gate.Wait();
        try
        {
            disposed = true;
            session.Dispose();
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private DecisionResult Choose(DecisionQuestion question, CancellationToken cancellationToken)
    {
        var sequence = LayaSequence.Build(tokenizer, tokens, settings, question);
        if (sequence.Markers.Count != question.Options.Count)
            throw new DecisionModelException("The options are too long for the model. Shorten the names and descriptions.");

        var length = sequence.Ids.Count;
        var count = sequence.Markers.Count;
        using var inputIds = OrtValue.CreateTensorValueFromMemory(sequence.Ids.Select(id => (long)id).ToArray(), [1, length]);
        using var attention = OrtValue.CreateTensorValueFromMemory(Enumerable.Repeat(1L, length).ToArray(), [1, length]);
        using var markerPositions = OrtValue.CreateTensorValueFromMemory(sequence.Markers.Select(marker => (long)marker).ToArray(), [1, count]);
        using var markerMask = OrtValue.CreateTensorValueFromMemory(Enumerable.Repeat(true, count).ToArray(), [1, count]);
        using var questionType = OrtValue.CreateTensorValueFromMemory(new long[] { ChoiceQuestionType }, [1]);

        using var run = new RunOptions();
        using var cancel = cancellationToken.Register(() => run.Terminate = true);
        try
        {
            using var outputs = session.Run(
                run,
                ["input_ids", "attention_mask", "marker_pos", "marker_mask", "qtype"],
                [inputIds, attention, markerPositions, markerMask, questionType],
                ["logits"]
            );

            var logits = outputs[0].GetTensorDataAsSpan<float>()[..count].ToArray();
            return Score(logits, settings.ChoiceTemperature(count));
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new DecisionModelException("The decision model failed.", ex);
        }
    }

    // A softmax of the scaled scores, and how far that is from a uniform guess.
    private static DecisionResult Score(IReadOnlyList<float> logits, double temperature)
    {
        var scaled = logits.Select(logit => logit / temperature).ToArray();
        var max = scaled.Max();
        var exponents = scaled.Select(value => Math.Exp(value - max)).ToArray();
        var sum = exponents.Sum();
        var probabilities = exponents.Select(value => value / sum).ToArray();
        return new(probabilities, DecisionResult.ConfidenceOf(probabilities));
    }
}
