using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Entities;

namespace Core.Tests;

public class EntityTestsTherapySession
{
    private TherapySession _sut = new TherapySession();

    public EntityTestsTherapySession()
    {
        _sut = new TherapySession()
            {
                Id = 1234,
                SessionDate = new DateTime(2024,4,18),  // April 18, 2024
                PatientId = 35,
                TherapistId = 3,
                Duration = 60,
                TherapyTypes = "TL/TS",
                Amount = 42.5m,
                AmountPaid = 40m,
                DiscountAmount = 2.5m,
                ProviderAmount = 17m,
                GrossProfit = 23m,
                IsPaidOff = true,
            };
    }

    [Fact]
    public void GetPastDue_Calculates_FALSE_OK()
    {
        // arrange
        //   use default...

        // act
        var pastDue = _sut.GetPastDue();

        // assert
        Assert.False(pastDue);
    }

    [Fact]
    public void GetPastDue_Calculates_TRUE_OK()
    {
        // arrange
        _sut.SessionDate = DateTime.UtcNow.AddDays(-46);
        _sut.AmountPaid = 0m;

        // act
        var pastDue = _sut.GetPastDue();

        // assert
        Assert.True(pastDue);
    }    
}