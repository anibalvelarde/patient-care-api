namespace Neurocorp.Api.Core.BusinessObjects.Common;

public interface IProfile
{
    int Id { get; }
    string Name { get; }
    bool IsValid { get; }
}