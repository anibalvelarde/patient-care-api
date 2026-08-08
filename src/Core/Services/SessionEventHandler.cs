using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Entities;
using System.Diagnostics;

namespace Neurocorp.Api.Core.Services;

public class SessionEventHandler : IHandleSessionEvent
{
    private readonly ILogger<SessionEventHandler> _logger;
    private readonly ISessionEventRepository _repository;
    private readonly ITherapySessionRepository _therapySessionRepository;
    private readonly IPatientProfileService _patientService;
    private readonly ITherapistProfileService _therapistService;
    private readonly IRepository<SpecialtyType> _specialtyTypeRepository;
    private readonly ITherapistSpecialtyRepository _therapistSpecialtyRepository;
    private readonly IPatientCaretakerRepository _patientCaretakerRepository;
    private readonly ISpecialtyPriceService _priceService;
    private readonly IRepository<Site> _siteRepository;
    private readonly ISessionTransitionMoneyService _transitionMoney;
    private readonly IUserNameResolver? _userNameResolver;

    public SessionEventHandler(ILogger<SessionEventHandler> logger, ISessionEventRepository repo, ITherapySessionRepository therapySessionRepo, ITherapistProfileService therapistSvc, IPatientProfileService patientSvc, IRepository<SpecialtyType> specialtyTypeRepo, ITherapistSpecialtyRepository therapistSpecialtyRepo, IPatientCaretakerRepository patientCaretakerRepo,
        // WP-40 (BK-2): booking money is server-derived — the price service resolves Amount from
        // the WP-39 sheet; the site repository supplies the on-site trip charge to snapshot.
        ISpecialtyPriceService priceService, IRepository<Site> siteRepo,
        // WP-42: covered-session lock + the 3/5 transition choke point on the generic PUT.
        // REQUIRED on purpose — an integrity guard must not silently vanish when unwired.
        ISessionTransitionMoneyService transitionMoney,
        // WP-31 (U1): optional so existing test constructions compile unchanged; DI supplies the real one.
        IUserNameResolver? userNameResolver = null)
    {
        _logger = logger;
        _repository = repo;
        _therapySessionRepository = therapySessionRepo;
        _patientService = patientSvc;
        _therapistService = therapistSvc;
        _specialtyTypeRepository = specialtyTypeRepo;
        _therapistSpecialtyRepository = therapistSpecialtyRepo;
        _patientCaretakerRepository = patientCaretakerRepo;
        _priceService = priceService;
        _siteRepository = siteRepo;
        _transitionMoney = transitionMoney;
        _userNameResolver = userNameResolver;
    }

    // WP-31 (U1): resolve audit updater names on the session-detail / appointments-by-date paths
    // (the popover surfaces). No-op when the resolver is absent or nothing carries audit.
    private async Task EnrichAuditAsync(IEnumerable<IHasAudit> items)
    {
        if (_userNameResolver != null)
        {
            await AuditEnrichment.ResolveNamesAsync(items, _userNameResolver);
        }
    }

    public async Task<IEnumerable<SessionEvent>> GetAllAsync()
    {
        return (await _repository.GetAllAsync()) ?? [];
    }


    public async Task<IEnumerable<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate)
    {
        _logger.LogInformation("Started to fetch Sessions for this date {targetDate}", targetDate);
        var stopwatch = Stopwatch.StartNew();

        // Starting to fetch
        stopwatch.Restart();
        var events = (await _repository.GetAllByTargetDateAsync(targetDate)) ?? [];
        _logger.LogInformation("Fetched events from repository in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);

        // Selecting and sorting events
        stopwatch.Restart();
        var sortedEvents = events
            .OrderBy(e => e.Patient)
            .ToList();
        _logger.LogInformation("Selected and sorted events in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);

        // WP-31 (U1): resolve audit updater names for the row popover (batched, single query).
        await EnrichAuditAsync(sortedEvents);

        // Returning the list to the caller
        _logger.LogInformation("Returning the list to the caller with {Count} events", sortedEvents.Count);
        return sortedEvents;
    }


    public async Task<IEnumerable<SessionEvent>> GetAllPastDueAsync()
    {
        _logger.LogInformation("Started to fetch Sessions for past-due events");
        var stopwatch = Stopwatch.StartNew();

        // WP-29: the repository filters candidates in SQL (money owed + date-only cutoff).
        var pastDueEvents = (await _repository.GetAllPastDueAsync()) ?? new List<SessionEvent>();
        var sortedPastDueEvents = SelectAndSortPastDue(pastDueEvents);

        _logger.LogInformation("Returning {Count} past-due events in {ElapsedMilliseconds} ms",
            sortedPastDueEvents.Count, stopwatch.ElapsedMilliseconds);
        return sortedPastDueEvents;
    }

    public async Task<IEnumerable<SessionEvent>> GetPastDueByPatientAsync(int patientId)
    {
        var candidates = (await _repository.GetAllPastDueAsync(patientId, therapistId: null)) ?? new List<SessionEvent>();
        return SelectAndSortPastDue(candidates);
    }

    public async Task<IEnumerable<SessionEvent>> GetPastDueByTherapistAsync(int therapistId)
    {
        var candidates = (await _repository.GetAllPastDueAsync(patientId: null, therapistId: therapistId)) ?? new List<SessionEvent>();
        return SelectAndSortPastDue(candidates);
    }

    // The exact past-due filter: the repository's SQL cutoff is date-only (a superset), so
    // boundary rows arrive with IsPastDue == false and are dropped here — output stays
    // identical to the pre-WP-29 full-table scan.
    private static List<SessionEvent> SelectAndSortPastDue(IEnumerable<SessionEvent> candidates)
        => candidates
            .Where(t => t.IsPastDue)
            .OrderByDescending(e => e.SessionDate)
            .ToList();

    public async Task<IEnumerable<PatientPastDueInfo>> GetAllPatientsPastDueAsync()
    {
        _logger.LogInformation("Started to fetch all patients with past-due sessions");
        var stopwatch = Stopwatch.StartNew();

        var pastDueSessions = await GetAllPastDueAsync();
        var groupedByPatient = pastDueSessions.GroupBy(s => s.PatientId).ToList();
        if (groupedByPatient.Count == 0)
        {
            _logger.LogInformation("No past-due sessions found.");
            return [];
        }

        // WP-29: one batched profile fetch instead of GetByIdAsync per past-due patient.
        // Group order (most recent past-due session first) is preserved by iterating the
        // groups, not the batch result.
        var profilesById = (await _patientService.GetByIdsAsync(groupedByPatient.Select(g => g.Key).ToList()))
            .ToDictionary(p => p.PatientId);

        var results = new List<PatientPastDueInfo>();
        foreach (var group in groupedByPatient)
        {
            if (!profilesById.TryGetValue(group.Key, out var patient)) continue;

            var sessions = group.ToList();
            results.Add(new PatientPastDueInfo
            {
                Party = patient,
                PastDueSessions = sessions.Count,
                PastDueTotalAmount = sessions.Sum(s => s.Amount - s.Discount),
                AmountPaidSoFar = sessions.Sum(s => s.AmountPaid),
                Delinquency = sessions,
            });
        }

        _logger.LogInformation("Found {Count} patients with past-due sessions in {ElapsedMilliseconds} ms",
            results.Count, stopwatch.ElapsedMilliseconds);
        return results;
    }

    public async Task<SessionEvent?> GetByIdAsync(int id)
    {
        var sessionEvent = await _repository.GetByIdAsync(id);
        if (sessionEvent != null)
        {
            // WP-31 (U1): the Session Details popover reads updatedBy from this path.
            await EnrichAuditAsync(new[] { sessionEvent });
        }
        return sessionEvent;
    }

    public async Task<SessionEvent> CreateAsync(SessionEvent entity)
    {
        return await Task.FromException<SessionEvent>(new NotImplementedException());
    }

    public async Task UpdateAsync(SessionEvent entity)
    {
        await Task.FromException<SessionEvent>(new NotImplementedException());
    }

    public async Task<SessionEvent> CreateAsync(SessionEventRequest request)
    {
        _logger.LogInformation("Started creating a new therapy session from request.");
        var (pProfile, tProfile) = await FetchTargetPartiesAsync(request);

        // WP-23 (F10): hard block — no caretaker on file, no booking (owner ruling: synthetic
        // placeholders satisfy the rule; they are PatientCaretaker links like any other).
        // ArgumentException → 400 via GlobalExceptionHandler.
        var caretakerLinks = await _patientCaretakerRepository.GetByPatientIdAsync(request.PatientId);
        if (!caretakerLinks.Any())
        {
            throw new ArgumentException("A caretaker must be linked to this patient before booking.");
        }

        // WP-40 (G1, 2026-07-27 addendum): fixed bookable durations — new bookings only; stored
        // legacy durations are never validated on reads. ArgumentException → 400.
        if (!SessionMoneyMath.IsBookableDuration(request.Duration))
        {
            throw new ArgumentException(
                $"Duration {request.Duration} min is not bookable — allowed: {SessionMoneyMath.BookableDurationsDisplay}.");
        }

        var specialty = await ResolveSpecialtyAsync(request);

        // Discovery-first enforcement: treatment specialties require a completed discovery session.
        // WP-24 (F3/F4): only when the patient's RequiresDiscovery flag is set — false = waived
        // (e.g. legacy-imported patients), which skips the check entirely.
        if (specialty != null && !specialty.IsDiscovery && pProfile!.RequiresDiscovery)
        {
            var hasDiscovery = await _therapySessionRepository.HasCompletedDiscoveryAsync(request.PatientId);
            if (!hasDiscovery)
                throw new ArgumentException(
                    "Patient requires a completed discovery session before treatment sessions can be scheduled.");
        }

        // Specialty validation: therapist must hold the requested specialty
        if (specialty != null && request.SpecialtyTypeId.HasValue)
        {
            var hasSpecialty = await _therapistSpecialtyRepository
                .HasTherapistSpecialtyAsync(request.TherapistId, specialty.Id);
            if (!hasSpecialty)
                throw new ArgumentException(
                    $"Therapist {tProfile!.TherapistName} does not have the {specialty.Name} qualification.");
        }

        // WP-40 (BK-2): the price sheet is the ONLY source of Amount — a booking without a
        // resolvable specialty has nothing to price against.
        if (specialty == null)
        {
            throw new ArgumentException(
                "A specialty is required — the session price is derived from the specialty's price sheet.");
        }

        // Amount: WP-39 resolution at the SESSION date (row → DefaultAmount → none ⇒ G4 block).
        var price = await _priceService.ResolvePriceAsync(specialty.Id, request.Duration, request.SessionDate);
        if (price.Amount is null)
        {
            throw new ArgumentException(SessionMoneyMath.MissingPriceMessage(specialty.Name, request.Duration));
        }
        var amount = price.Amount.Value;

        // Discount: G2 ruling (resolves WP-23 stacking) — exactly 20% when SENADIS is active at
        // the session date (WP-37 shared predicate), exactly 0 otherwise. Client money values
        // are ignored; log so junk senders are visible.
        var derivedDiscount = SessionMoneyMath.DeriveBookingDiscount(
            amount, pProfile!.HasActiveSenadisDiscount(request.SessionDate));
        if ((request.Amount != 0 && request.Amount != amount) || (request.Discount != 0 && request.Discount != derivedDiscount))
        {
            _logger.LogWarning(
                "WP-40: client-supplied money ignored on booking (amount {ReqAmount}→{Amount}, discount {ReqDiscount}→{Discount})",
                request.Amount, amount, request.Discount, derivedDiscount);
        }

        // On-site leg (WP-39 G4 ruling): eligibility lives on the specialty, the flat trip
        // charge on the Site — snapshotted at booking so later Site changes never reprice.
        decimal? onSiteCharge = null;
        if (request.IsOnSiteVisit)
        {
            if (!specialty.OfferedOnSite)
                throw new ArgumentException($"{specialty.Name} is not offered as an on-site visit.");
            if (!request.SiteId.HasValue)
                throw new ArgumentException("An on-site visit requires a site (the trip charge is configured per site).");
            var site = await _siteRepository.GetByIdAsync(request.SiteId.Value)
                ?? throw new ArgumentException($"Site {request.SiteId.Value} not found.");
            onSiteCharge = site.OnSiteTripChargeAmount;
        }

        var newTherapySession = await _therapySessionRepository.AddAsync(
            MapToNewSessionEvent(pProfile!, tProfile!, request, specialty, amount, derivedDiscount, onSiteCharge));
        _logger.LogInformation($"New TherapySession was created TSid:[{newTherapySession.Id}]");
        var confirmedStatuses = new HashSet<int> { 2, 4, 6, 7 };
        return new SessionEvent()
        {
            SessionId = newTherapySession.Id,
            SessionDate = newTherapySession.SessionDate,
            SessionTime = newTherapySession.SessionTime,
            Patient = pProfile!.PatientName,
            Therapist = tProfile!.TherapistName,
            TherapyTypes = newTherapySession.TherapyTypes,
            Amount = newTherapySession.Amount,
            Discount = newTherapySession.DiscountAmount,
            ProviderAmount = newTherapySession.ProviderAmount,
            AmountDue = newTherapySession.AmountDue(),
            IsPastDue = false,
            IsPaidOff = false,
            Notes = newTherapySession.Notes,
            AppointmentStatusId = newTherapySession.AppointmentStatusId,
            StatusName = newTherapySession.AppointmentStatus?.Name ?? "Proposed",
            IsConfirmed = confirmedStatuses.Contains(newTherapySession.AppointmentStatusId),
            SiteId = newTherapySession.SiteId,
            SiteName = newTherapySession.Site?.SiteName,
            SpecialtyTypeId = newTherapySession.SpecialtyTypeId,
            SpecialtyAbbreviation = specialty?.Abbreviation,
            SpecialtyName = specialty?.Name,
            IsDiscovery = specialty?.IsDiscovery,
            OnSiteChargeAmount = newTherapySession.OnSiteChargeAmount,
            // WP-49: a freshly created session never carries a fee, but state these explicitly
            // rather than leaning on defaults — this is the third of three mappers that must
            // stay in step, and "it happened to be false" is not the same as "it is false".
            LateFeeAmount = newTherapySession.LateFeeAmount,
            LateFeeAppliedOn = newTherapySession.LateFeeAppliedOn,
            FeeWaivedOn = newTherapySession.FeeWaivedOn,
            CarriesFee = newTherapySession.CarriesFee(),
            AmountSource = price.Source.ToWireString(),
        };
    }

    // WP-24 (audit F1): lets the controller ask "is this a discovery-specialty request?" BEFORE
    // CreateAsync, so the Patients.StartDiscovery gate can 403 with nothing created. Uses the
    // same resolution CreateAsync will run — Core stays free of HttpContext; the permission
    // check itself lives in SessionsController.
    public async Task<bool> IsDiscoveryRequestAsync(SessionEventRequest request)
    {
        var specialty = await ResolveSpecialtyAsync(request);
        return specialty?.IsDiscovery == true;
    }

    public async Task<bool> UpdateAsync(int sessionEventId, SessionEventUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(nameof(request));

        var onFile = await _therapySessionRepository.GetByIdAsync(sessionEventId);
        if (onFile is null) return false;

        // WP-40 (G1): validate the duration only when the edit actually CHANGES it — historical
        // (legacy-import) durations must never block an unrelated edit. Null = keep stored.
        if (request.Duration.HasValue && request.Duration.Value != onFile.Duration
            && !SessionMoneyMath.IsBookableDuration(request.Duration.Value))
        {
            throw new ArgumentException(
                $"Duration {request.Duration.Value} min is not bookable — allowed: {SessionMoneyMath.BookableDurationsDisplay}.");
        }

        // WP-42 (G5): covered-session lock — while non-reversed payroll allocations exist,
        // amount/discount changes AND therapist reassignment are hard-blocked. This runs
        // BEFORE the WP-40 recompute below, closing the known gap where a gated discount edit
        // silently changed ProviderAmount on an already-paid session. Echoed-unchanged values
        // pass; non-money edits stay allowed.
        var amountChanged = request.Amount != onFile.Amount;
        var discountChanged = request.Discount != onFile.DiscountAmount;
        var therapistChanged = request.TherapistId.HasValue && request.TherapistId.Value != onFile.TherapistId;
        if ((amountChanged || discountChanged || therapistChanged)
            && await _transitionMoney.IsCoveredByServicePaymentAsync(sessionEventId))
        {
            throw new ArgumentException(SessionTransitionMoneyService.CoveredSessionMessage);
        }

        // WP-42: the generic-PUT leg of the money-at-transition choke point — an
        // appointmentStatusId that CHANGES to Cancelled (3) / NoShow (5) applies the same
        // guards + money side-effects as the dedicated endpoints. The patch values override
        // whatever money the client sent, and deliberately BYPASS the WP-40 derivation below
        // (no SENADIS floor, no fee recompute on the zero/fee write itself).
        var transitionsToMoneyStatus = request.AppointmentStatusId.HasValue
            && (request.AppointmentStatusId.Value == SessionTransitionMoneyService.CancelledStatusId
                || request.AppointmentStatusId.Value == SessionTransitionMoneyService.NoShowStatusId)
            && request.AppointmentStatusId.Value != onFile.AppointmentStatusId;

        SessionMoneyPatch? moneyPatch = null;
        if (transitionsToMoneyStatus)
        {
            var patch = await _transitionMoney.PrepareTransitionAsync(onFile, request.AppointmentStatusId!.Value);
            if (patch is not null)
            {
                request.Amount = patch.Amount;
                request.Discount = patch.DiscountAmount;
                moneyPatch = new SessionMoneyPatch(patch.ProviderAmount, patch.GrossProfit);
                if (!string.IsNullOrEmpty(patch.NotesMarker))
                {
                    request.Notes = string.IsNullOrWhiteSpace(request.Notes)
                        ? patch.NotesMarker
                        : $"{request.Notes}\n{patch.NotesMarker}";
                }
            }
            else
            {
                // Idempotent transition (already zeroed / fee already applied): status-only —
                // stored money stays byte-identical.
                request.Amount = onFile.Amount;
                request.Discount = onFile.DiscountAmount;
            }
        }
        // WP-40 (BK-3): when the edit changes Amount or Discount, validate the FINAL pair and
        // re-derive ProviderAmount/GrossProfit through the shared math (never a stale fee after
        // a discount change). The Sessions.Discount.Edit claim gate lives in SessionsController.
        // When neither changes, stored money stays byte-identical (WP-29 discipline).
        else if (amountChanged || discountChanged)
        {
            var patient = await _patientService.GetByIdAsync(onFile.PatientId);
            if (patient?.HasActiveSenadisDiscount(onFile.SessionDate) == true)
            {
                var floor = SessionMoneyMath.SenadisFloor(request.Amount);
                if (request.Discount < floor)
                {
                    throw new ArgumentException(
                        $"Discount {request.Discount:0.00} is below the SENADIS floor {floor:0.00} " +
                        $"(20% of {request.Amount:0.00}) — an active-SENADIS session may be discounted more, never less.");
                }
            }
            else if (request.Discount < 0 || request.Discount > request.Amount)
            {
                throw new ArgumentException("Discount must be between 0 and the session amount.");
            }

            var therapistId = request.TherapistId ?? onFile.TherapistId;
            var tProfile = await _therapistService.GetByIdAsync(therapistId)
                ?? throw new ArgumentException($"Therapist {therapistId} not found.");
            var net = request.Amount - request.Discount;
            var fee = tProfile.CalculateFee(net);
            // WP-49 Finding 1 — the highest-risk line in the WP. This recompute fires on EVERY
            // discount edit, long after a late fee was applied. Passing onFile.LateFeeAmount
            // keeps the fee in GrossProfit; dropping it would leave the fee collectible in
            // AmountDue() while it silently vanished from clinic P&L, correct on day one and
            // wrong forever after, with nothing to signal the divergence.
            moneyPatch = new SessionMoneyPatch(fee,
                SessionMoneyMath.GrossProfit(net, fee, onFile.OnSiteChargeAmount, onFile.LateFeeAmount));
        }

        await _repository.UpdateAsync(sessionEventId, request, moneyPatch);
        return true;
    }

    public Task DeleteAsync(int id)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> VerifyRequestAsync(int sessionAggId, SessionEventUpdateRequest request)
    {
        var profile = await this.GetByIdAsync(sessionAggId);
        if (profile != null)
        {
            return true;
        }
        return false;
    }

    private static TherapySession MapToNewSessionEvent(PatientProfile pProfile, TherapistProfile tProfile,
        SessionEventRequest req, SpecialtyType specialty, decimal amount, decimal discount, decimal? onSiteCharge)
    {
        // WP-23 (Questionnaire E ruling): the therapist fee applies to the NET amount — after
        // discounts — matching BulkSchedulingService. WP-40: amount/discount arrive derived
        // (price sheet + SENADIS rule); the on-site trip charge is excluded from the fee base
        // and added to gross profit.
        var netAmount = amount - discount;
        var calcProviderAmt = tProfile.CalculateFee(netAmount);
        // WP-49: lateFee is null because this is the CREATE path — a session being booked
        // cannot already carry a late chargeback. Stated explicitly rather than defaulted.
        var calcGrossProfit = SessionMoneyMath.GrossProfit(netAmount, calcProviderAmt, onSiteCharge, lateFee: null);
        return new TherapySession()
        {
            PatientId = pProfile.PatientId,
            TherapistId = tProfile.TherapistId,
            SessionDate = req.SessionDate,
            SessionTime = req.SessionTime,
            TherapyTypes = specialty.Abbreviation,
            Duration = req.Duration,
            Amount = amount,
            DiscountAmount = discount,
            ProviderAmount = calcProviderAmt,
            GrossProfit = calcGrossProfit,
            Notes = req.Notes,
            AppointmentStatusId = req.AppointmentStatusId,
            SiteId = req.SiteId,
            SpecialtyTypeId = specialty.Id,
            OnSiteChargeAmount = onSiteCharge,
        };
    }

    private async Task<SpecialtyType?> ResolveSpecialtyAsync(SessionEventRequest request)
    {
        // If SpecialtyTypeId is provided, use it directly
        if (request.SpecialtyTypeId.HasValue)
        {
            var specialty = await _specialtyTypeRepository.GetByIdAsync(request.SpecialtyTypeId.Value);
            if (specialty == null)
            {
                _logger.LogWarning("SpecialtyTypeId {Id} not found, ignoring", request.SpecialtyTypeId.Value);
            }
            return specialty;
        }

        // If only free-text TherapyType is provided, try to resolve by abbreviation
        if (!string.IsNullOrEmpty(request.TherapyType) && request.TherapyType != "N/A")
        {
            var allSpecialties = await _specialtyTypeRepository.GetAllAsync();
            var matched = allSpecialties.FirstOrDefault(s =>
                s.Abbreviation.Equals(request.TherapyType, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                _logger.LogInformation("Resolved free-text TherapyType '{Type}' to SpecialtyTypeId {Id}", request.TherapyType, matched.Id);
            }
            return matched;
        }

        return null;
    }

    public async Task<IEnumerable<DiscoverySessionSummary>> GetCompletedDiscoverySessionsAsync(int patientId)
    {
        var sessions = await _therapySessionRepository.GetCompletedDiscoverySessionsAsync(patientId);
        return sessions.Select(s => new DiscoverySessionSummary
        {
            SessionId = s.Id,
            SessionDate = s.SessionDate,
            SpecialtyAbbreviation = s.SpecialtyType?.Abbreviation ?? "N/A",
            TherapistName = s.Therapist?.User?.GetFullName() ?? "Unknown",
        });
    }

    private async Task<(PatientProfile?, TherapistProfile?)> FetchTargetPartiesAsync(SessionEventRequest request)
    {
        return await FetchTargetPartiesAsync(request.PatientId, request.TherapistId);
    }

    private async Task<(PatientProfile?, TherapistProfile?)> FetchTargetPartiesAsync(int patientId, int therapistId)
    {
        var verifyPatient = await _patientService.GetByIdAsync(patientId);
        var verifyTherapist = await _therapistService.GetByIdAsync(therapistId);

        if (verifyPatient is null)
        {
            throw new ArgumentNullException(nameof(patientId), "Patient not found.");
        }
        
        if (verifyTherapist is null)
        {
            throw new ArgumentNullException(nameof(therapistId), "Therapist not found.");
        }

        return (verifyPatient, verifyTherapist);
    }
}