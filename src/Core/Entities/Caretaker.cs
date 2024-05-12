namespace Neurocorp.Api.Core.Entities;

public class Caretaker
{
    public Caretaker()
    {
        this.Name = "";
        this.Relationship = "";
    }
    public int Id {get; set;}
    public string Name {get; set;}
    public string Relationship {get; set;}
}