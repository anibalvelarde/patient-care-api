namespace Neurocorp.Api.Core.Entities;

public class Patient
{
    public Patient()
    {
        this.Name = "";
    }
    public int Id {get; set;}
    public string Name {get; set;}
    public DateTime DateOfBirth {get; set;}
}