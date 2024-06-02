using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using System;

namespace Core.Tests;

public class BusinessObjectTestsTherapistProfile
{
    private TherapistProfile _sut;

    public BusinessObjectTestsTherapistProfile()
    {
        _sut = new TherapistProfile()
            {
                TherapistId = 1000,
                UserId = 2000,
                FeePctPerSession = 0.35m,
                FeePerSession = 28m,
                TherapistName = $"Tests-{DateTime.UtcNow}",
                Email = "some@email.com",
                PhoneNumber = "777-867-5309",
                CreatedTimestamp = DateTime.UtcNow,
                IsActive = true,
            };
    }

    [Fact]
    public void FeeAmount_ByPct_Calculates_OK()
    {
        // arrange
        var testSessionAmount = 100m;
        decimal feePctSetting = (decimal)DateTime.UtcNow.Millisecond/1000;
        var expFeePct = testSessionAmount * feePctSetting;
        _sut.FeePctPerSession = feePctSetting;

        // act
        var calculatedFeePct = _sut.CalculateFee(testSessionAmount);

        // assert
        Assert.Equal(expFeePct, calculatedFeePct);
    }
}