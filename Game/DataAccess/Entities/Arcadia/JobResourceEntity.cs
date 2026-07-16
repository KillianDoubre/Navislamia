namespace Navislamia.Game.DataAccess.Entities.Arcadia;

public class JobResourceEntity : Entity
{
    public int StatId { get; set; }
    public int JobClass { get; set; }
    public short JobDepth { get; set; }
    public short UpLv { get; set; }
    public short UpJlv { get; set; }
    public short[] AvailableJobs { get; set; }
}
