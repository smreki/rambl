# Regenerates the Qwen3-TTS entry from anonymous Hugging Face metadata. Never invent hashes or revisions.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = 'audio-cpp/audio.cpp-gguf'
$file = 'Qwen3-TTS-12Hz-1.7B-CustomVoice-GGUF/qwen3-tts-12hz-1.7b-customvoice-q8_0.gguf'
$metadata = Invoke-RestMethod "https://huggingface.co/api/models/${repo}?blobs=true"
if ($metadata.private -or $metadata.gated -ne $false -or $metadata.sha -notmatch '^[a-f0-9]{40}$') {
    throw 'The model repository must be public, ungated and pinned to a commit.'
}
$entry = $metadata.siblings | Where-Object rfilename -eq $file
if ($entry.lfs.sha256 -notmatch '^[a-f0-9]{64}$' -or $entry.size -le 0) {
    throw 'Hugging Face did not return the file size and SHA-256.'
}
$source = 'Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice'
$sourceMetadata = Invoke-RestMethod "https://huggingface.co/api/models/$source"
$config = Invoke-RestMethod "https://huggingface.co/$source/raw/$($sourceMetadata.sha)/config.json"
$speakers = @(
    @('Aiden', 'aiden', 'male'), @('Dylan', 'dylan', 'male'), @('Eric', 'eric', 'male'),
    @('Ono_Anna', 'ono_anna', 'female'), @('Ryan', 'ryan', 'male'), @('Serena', 'serena', 'female'),
    @('Sohee', 'sohee', 'female'), @('Uncle_Fu', 'uncle_fu', 'male'), @('Vivian', 'vivian', 'female')
)
$voices = foreach ($speaker in $speakers) {
    $speakerId = $config.talker_config.spk_id.($speaker[1])
    if ($null -eq $speakerId) { throw "Unknown speaker $($speaker[0])." }
    [ordered]@{ id = $speaker[0]; name = $speaker[0].Replace('_', ' '); speaker = $speakerId; gender = $speaker[2] }
}
$model = [ordered]@{
    id = 'qwen3-tts-1.7b-customvoice'
    name = 'Qwen3-TTS 1.7B CustomVoice'
    family = 'qwen3-tts-customvoice'
    purpose = [ordered]@{ label = 'Expressive multilingual voices'; icon = 'sparkles' }
    cpuFit = 'heavy'
    languages = [ordered]@{ label = '10 languages'; codes = @('en', 'zh', 'ja', 'ko', 'de', 'fr', 'ru', 'pt', 'es', 'it') }
    license = [ordered]@{ id = 'Apache-2.0' }
    source = "https://huggingface.co/$source"
    notes = @(
        'Nine built-in voices, including Aiden. Runs locally with audio.cpp; no Python or API key is needed.',
        'Choose Automatic, CPU or GPU (Vulkan) on the Local model card. Automatic prefers Vulkan and falls back to CPU when GPU loading fails.',
        'The self-contained Q8_0 GGUF includes the speech tokenizer. The first sentence warms the inference caches.',
        'Speed is adjusted locally with pitch-preserving overlap-add. Voice cloning and voice-design checkpoints are not supported.'
    )
    parameters = '1.7B'
    defaultVoice = 'Aiden'
    voices = @($voices)
    artifacts = @([ordered]@{
        id = 'audio-cpp-q8_0'; format = 'gguf'; quantization = 'q8_0'
        runtimes = @([ordered]@{ name = 'audio.cpp' })
        host = [ordered]@{ type = 'huggingface'; repo = $repo; revision = $metadata.sha }
        files = @([ordered]@{ path = $file; bytes = $entry.size; sha256 = $entry.lfs.sha256 })
    })
}
$catalogPath = Join-Path $PSScriptRoot '../src/Resources/Raw/tts-models.json'
$catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json -AsHashtable
$catalog.generatedAt = (Get-Date).ToString('yyyy-MM-dd')
$catalog.runtimes = @($catalog.runtimes | Where-Object { $_.id -ne 'audio.cpp' }) + @([ordered]@{
    id = 'audio.cpp'; name = 'audio.cpp'; formats = @('gguf'); platforms = @('windows-x64')
    automaticOrder = 2; license = 'Apache-2.0'; project = 'https://github.com/0xShug0/audio.cpp'
})
$catalog.models = @($catalog.models | Where-Object { $_.id -ne $model.id }) + @($model)
$json = ($catalog | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n"
[IO.File]::WriteAllText([IO.Path]::GetFullPath($catalogPath), $json, [Text.UTF8Encoding]::new($false))
Write-Output "Pinned $repo at $($metadata.sha): $($entry.size) bytes, $($entry.lfs.sha256)"
