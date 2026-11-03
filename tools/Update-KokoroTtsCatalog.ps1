# Adds the audio.cpp Kokoro artifact without replacing the existing sherpa-onnx CPU files or voice IDs.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = 'audio-cpp/audio.cpp-gguf'
$file = 'Kokoro-82M-GGUF/kokoro-82m-q8_0.gguf'
$metadata = Invoke-RestMethod "https://huggingface.co/api/models/${repo}?blobs=true"
if ($metadata.private -or $metadata.gated -ne $false -or $metadata.sha -notmatch '^[a-f0-9]{40}$') {
    throw 'The model repository must be public, ungated and pinned to a commit.'
}
$entry = $metadata.siblings | Where-Object rfilename -eq $file
if ($entry.lfs.sha256 -notmatch '^[a-f0-9]{64}$' -or $entry.size -le 0) {
    throw 'Hugging Face did not return the file size and SHA-256.'
}
$path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Resources/Raw/tts-models.json'))
$catalog = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -AsHashtable
$kokoro = $catalog.models | Where-Object id -eq 'kokoro-82m'
$kokoro.artifacts = @($kokoro.artifacts | Where-Object id -ne 'audio-cpp-q8_0') + @([ordered]@{
    id = 'audio-cpp-q8_0'; format = 'gguf'; quantization = 'q8_0'
    runtimes = @([ordered]@{ name = 'audio.cpp' })
    host = [ordered]@{ type = 'huggingface'; repo = $repo; revision = $metadata.sha }
    files = @([ordered]@{ path = $file; bytes = $entry.size; sha256 = $entry.lfs.sha256 })
})
$note = 'CPU uses the installed sherpa-onnx artifact when available. Choosing GPU downloads the audio.cpp Q8_0 GGUF and uses Vulkan; Automatic prefers audio.cpp once downloaded. The same 28 English voice IDs work in both runtimes. audio.cpp uses its native speed control and bundled eSpeak phonemizer.'
$kokoro.notes = @($kokoro.notes | Where-Object { $_ -notlike 'CPU uses the installed sherpa-onnx artifact*' }) + @($note)
$catalog.generatedAt = (Get-Date).ToString('yyyy-MM-dd')
$json = ($catalog | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n"
[IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
Write-Output "Pinned Kokoro at $($metadata.sha): $($entry.size) bytes, $($entry.lfs.sha256)"
