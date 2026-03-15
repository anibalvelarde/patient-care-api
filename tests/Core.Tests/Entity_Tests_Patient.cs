using Neurocorp.Api.Core.Entities;

namespace Core.Tests;

public class EntityTestsPatient
{
    [Fact]
    public void HasTemporaryMrn_EmptyString_ReturnsTrue()
    {
        var patient = new Patient { MedicalRecordNumber = "" };
        Assert.True(patient.HasTemporaryMrn());
    }

    [Fact]
    public void HasTemporaryMrn_Null_ReturnsTrue()
    {
        var patient = new Patient { MedicalRecordNumber = null! };
        Assert.True(patient.HasTemporaryMrn());
    }

    [Fact]
    public void HasTemporaryMrn_TempPrefix_ReturnsTrue()
    {
        var patient = new Patient { MedicalRecordNumber = "TEMP-42" };
        Assert.True(patient.HasTemporaryMrn());
    }

    [Fact]
    public void HasTemporaryMrn_RealMrn_ReturnsFalse()
    {
        var patient = new Patient { MedicalRecordNumber = "MRN-001" };
        Assert.False(patient.HasTemporaryMrn());
    }
}
