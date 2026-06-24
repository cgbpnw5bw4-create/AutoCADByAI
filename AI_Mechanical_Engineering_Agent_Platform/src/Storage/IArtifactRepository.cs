using DomainSchemas;

namespace Storage;

public interface IArtifactRepository
{
    Task SaveAsync(ArtifactInfo artifact, CancellationToken cancellationToken = default);

    Task<ArtifactInfo?> GetAsync(string artifactId, CancellationToken cancellationToken = default);
}
