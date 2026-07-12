using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>Request body for both merge endpoints (WP-22): preview and execute.</summary>
public class PatientMergeRequest
{
    [Range(1, int.MaxValue)]
    public int SurvivorPatientId { get; set; }

    [Range(1, int.MaxValue)]
    public int EliminatedPatientId { get; set; }
}
