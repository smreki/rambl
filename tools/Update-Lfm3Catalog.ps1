# Regenerates native LFM3 Text and the one shared Omni installation from Hugging Face metadata.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$path = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Resources/Raw/language-models.json'))
$catalog = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -AsHashtable
$catalog.models = @($catalog.models | Where-Object id -ne 'lfm3-omni-2b')
foreach ($spec in @(
    @{ id = 'lfm3-1.7b'; name = 'LFM3 1.7B'; repo = 'LFM3-1.7B-GGUF'; file = 'LFM3-1.7B-Q4_K_M.gguf'; context = 262144; parameters = '1.7B'; label = 'Local text generation' },
    @{ id = 'lfm3-omni-2b'; name = 'LFM3 Omni 2B'; repo = 'LFM3-Omni-2B-GGUF'; file = 'LFM3-Omni-2B-Q4_K_M.gguf'; context = 32768; parameters = '2.2B'; label = 'Transcribe, rewrite and read aloud' }
)) {
    $repo = "LiquidAI/$($spec.repo)"
    $metadata = Invoke-RestMethod "https://huggingface.co/api/models/${repo}?blobs=true"
    if ($metadata.private -or $metadata.gated -ne $false -or $metadata.sha -notmatch '^[a-f0-9]{40}$') {
        throw 'The model repository must be public, ungated and pinned to a commit.'
    }
    $file = $metadata.siblings | Where-Object rfilename -eq $spec.file
    if ($file.lfs.sha256 -notmatch '^[a-f0-9]{64}$' -or $file.size -le 0) {
        throw 'Hugging Face did not return the file size and SHA-256.'
    }
    $model = [ordered]@{
        id = $spec.id; name = $spec.name; family = 'lfm3'; kind = 'chat'; modelType = 'llm'
        tasks = @('formatting', 'translation')
        purpose = [ordered]@{ label = $spec.label; icon = 'feather' }
        cpuFit = 'good'; parameters = $spec.parameters; contextLength = $spec.context
        languages = [ordered]@{ label = "$($metadata.cardData.language.Count) languages"; codes = @($metadata.cardData.language) }
        license = [ordered]@{
            id = 'LFM-Open-1.0'
            attribution = 'Liquid AI · LFM Open License v1.0. Use is subject to the license terms, including its revenue and funding threshold.'
        }
        source = "https://huggingface.co/$repo"
        notes = @(
            'Runs natively inside Rambl with the pinned llama.cpp bridge. Text generation only; no model server is started.'
            'The trained context is shown here; Rambl currently allocates at most 8192 tokens.'
        )
        artifacts = @([ordered]@{
            id = 'gguf-q4_k_m'; format = 'gguf'; quantization = 'q4_k_m'
            runtimes = @([ordered]@{ name = 'llama.cpp' })
            host = [ordered]@{ type = 'huggingface'; repo = $repo; revision = $metadata.sha }
            files = @([ordered]@{ path = $spec.file; bytes = $file.size; sha256 = $file.lfs.sha256 })
        })
    }
    if ($spec.id -eq 'lfm3-omni-2b') {
        $model.family = 'lfm3-omni'
        $model.capabilities = @('stt', 'text', 'tts')
        $model.languages = [ordered]@{ label = '16 text languages'; codes = @('en','ar','zh','fr','de','it','ja','ko','pt','es','vi','th','id','hi','ru','pl') }
        $model.audioLanguages = [ordered]@{ label = '18 speech languages'; codes = @('en-us','en-gb','ar','da','de','el','fi','fr','he','it','ja','ko','nl','pl','pt','es','sv','tr','zh'); selectable = $true }
        $model.defaultVoice = 'female'
        $model.voices = @(
            [ordered]@{ id = 'female'; name = 'Female'; speaker = 0; gender = 'female' },
            [ordered]@{ id = 'male'; name = 'Male'; speaker = 1; gender = 'male' }
        )
        $projector = $metadata.siblings | Where-Object rfilename -eq 'mmproj-LFM3-Omni-2B-Q8_0.gguf'
        if ($projector.lfs.sha256 -notmatch '^[a-f0-9]{64}$' -or $projector.size -le 0) { throw 'Missing Omni audio/projector metadata.' }
        $model.artifacts[0].files += [ordered]@{ path = $projector.rfilename; bytes = $projector.size; sha256 = $projector.lfs.sha256 }
        $model.notes = @(
            'One installation and one native backbone serve STT, text post-processing and TTS. Includes the matching Q8_0 audio/vision projector and codec components. Vision is not exposed in Rambl.'
            'Native CPU inference; requests share one execution queue. ASR processes bounded segments, and speech output is mono 24 kHz. Female and male voices support the listed speech languages.'
            'The trained context is 32768 tokens; Rambl currently allocates 8192.'
        )
        $model.notes += 'd1-Omni is a separate decision checkpoint; calibrated decisions are not provided by this generative checkpoint.'
        $shared = [ordered]@{
            schemaVersion = 1; generatedAt = (Get-Date).ToString('yyyy-MM-dd')
            runtimes = @($catalog.runtimes | Where-Object id -eq 'llama.cpp')
            modelTypes = @($catalog.modelTypes | Where-Object id -eq 'llm')
            tasks = @($catalog.tasks | Where-Object id -in @('formatting','translation'))
            models = @($model)
        }
        $sharedPath = Join-Path $PSScriptRoot '../src/Resources/Raw/multimodal-models.json'
        [IO.File]::WriteAllText($sharedPath, ($shared | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
    } else {
        $catalog.models = @($catalog.models | Where-Object id -ne $spec.id) + @($model)
    }
    Write-Output "Pinned $repo at $($metadata.sha): $($file.size) bytes, $($file.lfs.sha256)"
}
$catalog.generatedAt = (Get-Date).ToString('yyyy-MM-dd')
[IO.File]::WriteAllText($path, ($catalog | ConvertTo-Json -Depth 100).Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
