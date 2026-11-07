using System.Text;

namespace AIUpdater.Models;

public sealed class VendorRun(string vendor, IReadOnlyList<UpdateStage> stages)
{
    public string Vendor { get; } = vendor;
    public IReadOnlyList<UpdateStage> Stages { get; } = stages;
    public UpdateStage? ActiveStage { get; set; }
    public bool SawError { get; set; }
    public HashSet<string> ResultStages { get; } = [];
    public StringBuilder OutputBuffer { get; } = new();
    public StringBuilder ErrorBuffer { get; } = new();
}
