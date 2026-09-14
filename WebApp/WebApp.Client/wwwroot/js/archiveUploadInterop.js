// Minimal, decision-free bridge for resumable archive uploads: it only slices a browser File and
// hands the slice back to .NET as a JS stream reference. All session/offset/retry/progress logic
// stays in C# (ArchiveBrowser.razor + ArchiveUploadItemState).

export function sliceToStreamRef(inputElement, fileIndex, offset, length) {
    const file = inputElement?.files?.[fileIndex];
    if (!file) {
        return null;
    }

    // Return the raw Blob: Blazor's own interop runtime wraps a JS-function return value into
    // IJSStreamReference on the .NET side when the call site expects that type. Manually calling
    // DotNet.createJSStreamReference here double-wraps the value and throws "Supplied value is
    // not a typed array or blob." on the .NET side.
    return file.slice(offset, offset + length);
}

export function getSelectedFileInfo(inputElement, fileIndex) {
    const file = inputElement?.files?.[fileIndex];
    if (!file) {
        return null;
    }

    return { name: file.name, size: file.size };
}
