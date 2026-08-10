namespace DatingApp.Api.Storage;

/// Where uploaded photos actually live. Local disk is fine for development, but every
/// free-tier host has ephemeral disk, so production points this at object storage.
public interface IPhotoStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// Returns null when the object no longer exists.
    Task<Stream?> OpenAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
