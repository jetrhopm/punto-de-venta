using Pos.Domain;

namespace Pos.Application;

public static class SaleDraftFactory
{
    public static SaleDraft Create(DateTimeOffset nowUtc) =>
        new(Guid.NewGuid(), Guid.NewGuid(), nowUtc);
}
