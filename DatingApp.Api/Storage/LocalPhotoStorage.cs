namespace DatingApp.Api.Storage;

/// Development and test storage: writes beside the app. Does not survive a container
/// restart, which is why production uses S3PhotoStorage instead.
public class LocalPhotoStorage : IPhotoStorage
{
    private readonly string _root;

    public LocalPhotoStorage(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    // Keys are server-generated, but resolve anyway so a malformed key can never
    // escape the upload directory.
    private string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        var root = Path.GetFullPath(_root);
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException("Key resolves outside the storage root.", nameof(key));
        return full;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await using var file = File.Create(PathFor(key));
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(
            File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
