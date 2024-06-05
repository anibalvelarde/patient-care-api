namespace Neurocorp.Api.Core.BusinessObjects.Common;

public class NotFoundProfile : IProfile
{
    public int Id => -1;

    public string Name => "NotFound";

    public bool IsValid => false;
}