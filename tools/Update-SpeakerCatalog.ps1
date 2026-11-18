# Writes the speaker-label (diarization) catalog used by meeting transcription from the Hugging Face API.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = 'mudler/parakeet-cpp-gguf'
$file = 'nemotron-3-diarization-q8_0.gguf'
$metadata = Invoke-RestMethod "https://huggingface.co/api/models/${repo}?blobs=true"
if ($metadata.private -or $metadata.gated -ne $false -or $metadata.sha -notmatch '^[a-f0-9]{40}$') {
    throw 'The model repository must be public, ungated and pinned to a commit.'
}
$entry = $metadata.siblings | Where-Object rfilename -eq $file
if ($entry.lfs.sha256 -notmatch '^[a-f0-9]{64}$' -or $entry.size -le 0) {
    throw 'Hugging Face did not return the file size and SHA-256.'
}
$catalog = [ordered]@{
    schemaVersion = 1
    generatedAt = (Get-Date).ToString('yyyy-MM-dd')
    runtimes = @([ordered]@{
        id = 'parakeet.cpp'; name = 'parakeet.cpp'; formats = @('gguf')
        platforms = @('windows-x64', 'macos-arm64', 'macos-x64'); automaticOrder = 1
        license = 'MIT'; project = 'https://github.com/mudler/parakeet.cpp'
    })
    models = @([ordered]@{
        id = 'nemotron-3-diarization'
        name = 'Nemotron 3 Diarization'
        family = 'sortformer'
        purpose = [ordered]@{ label = 'Speaker labels'; icon = 'users' }
        cpuFit = 'good'
        languages = [ordered]@{ label = 'Any language'; codes = @() }
        license = [ordered]@{ id = 'OpenMDW-1.1' }
        source = 'https://huggingface.co/nvidia/Nemotron-3-Diarization'
        ramMbEstimate = @(300, 600)
        notes = @('Streaming Sortformer for up to eight speakers. Meeting transcription uses it to label the voices in the call audio.')
        artifacts = @([ordered]@{
            id = 'parakeet-cpp-q8_0'; format = 'gguf'; quantization = 'q8_0'
            runtimes = @([ordered]@{ name = 'parakeet.cpp' })
            host = [ordered]@{ type = 'huggingface'; repo = $repo; revision = $metadata.sha }
            files = @([ordered]@{ path = $file; bytes = $entry.size; sha256 = $entry.lfs.sha256 })
        })
    })
}
$path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Resources/Raw/speaker-models.json'))
$json = ($catalog | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n"
[IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
Write-Output "Pinned Nemotron 3 Diarization at $($metadata.sha): $($entry.size) bytes, $($entry.lfs.sha256)"
