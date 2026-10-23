using System.Text.Json.Serialization;

namespace Rambl.Features.Models;

/// <summary>Whether a runtime can transcribe a model while the user is still speaking.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StreamingSupport>))]
public enum StreamingSupport
{
    /// <summary>Only whole recordings can be transcribed.</summary>
    None,

    /// <summary>The runtime fakes live text by re-transcribing a sliding window; it is not shown as Streaming.</summary>
    Emulated,

    /// <summary>The model and runtime stream natively.</summary>
    Native,
}
