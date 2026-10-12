import io
import re

def patch(path, old, new, count=1, optional=False):
    s = io.open(path, encoding="utf-8").read()
    if old in s:
        s = s.replace(old, new, count)
        io.open(path, "w", encoding="utf-8", newline="\n").write(s)
        print("patched", path.split("/")[-1])
        return True
    if optional:
        print("skip (absent)", path.split("/")[-1])
        return False
    raise AssertionError("anchor missing in " + path)

# ── 1. FamilyManager restoration stubs (compile-derived from FamilyActionPolicy usage) ──
p = "src/MealMate.Application/Features/FamilyManager/FamilyManagerContracts.cs"
s = io.open(p, encoding="utf-8").read()
if "FamilyCapabilityDto" not in s:
    s += """
// ── Restoration stubs (2026-10-06) ───────────────────────────────────────────
// The consolidation commit shipped FamilyActionPolicy.cs referencing these two
// contracts without their declarations, breaking the whole solution build. The
// shapes below are derived strictly from the existing usage in
// FamilyActionPolicy.GetMyFamilyCapabilitiesHandler; the owning feature session
// should replace them with the intended definitions.

/// <summary>Query: the current actor's capability decisions for one family member.</summary>
public sealed record GetMyFamilyCapabilitiesQuery(Guid MemberId)
    : MealMate.Application.Common.Messaging.IQuery<IReadOnlyList<FamilyCapabilityDto>>;

/// <summary>One action's capability decision as rendered by the family UI.</summary>
public sealed record FamilyCapabilityDto(
    string Action,
    bool Allowed,
    string ReasonCode,
    string? RequiredPermission);
"""
    io.open(p, "w", encoding="utf-8", newline="\n").write(s)
    print("family stubs appended")

# ── 2. Messaging.cs: ISelfPersistingCommand ──
patch(
    "src/MealMate.Application/Common/Messaging/Messaging.cs",
    "/// <summary>Marker so pipeline behaviors (transaction) can target commands only.</summary>\npublic interface IBaseCommand;",
    "/// <summary>Marker so pipeline behaviors (transaction) can target commands only.</summary>\npublic interface IBaseCommand;\n\n"
    "/// <summary>\n"
    "/// Marker for commands that own their persistence: the handler (or its mutation\n"
    "/// executor) commits inside its own transaction/lock, so UnitOfWorkBehavior must\n"
    "/// not run a second save. A second save after reconciliation can resurrect a stale\n"
    "/// tracked entity and crash the pipeline (F04/V07).\n"
    "/// </summary>\n"
    "public interface ISelfPersistingCommand : IBaseCommand;",
)

# ── 3. UnitOfWorkBehavior skip ──
patch(
    "src/MealMate.Application/Common/Behaviors/UnitOfWorkBehavior.cs",
    "        if (request is not IBaseCommand)\n        {\n            return await next();\n        }",
    "        if (request is not IBaseCommand || request is ISelfPersistingCommand)\n        {\n"
    "            // Queries never persist here, and self-persisting commands commit inside\n"
    "            // their own lock/transaction — a second save would write stale tracked\n"
    "            // state the handler already reconciled (F04/V07).\n"
    "            return await next();\n"
    "        }",
)

# ── 4. IAppDbContext + AppDbContext: DetachTrackedEntity/ReloadEntityAsync ──
patch(
    "src/MealMate.Application/Common/Abstractions/IAppDbContext.cs",
    "    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);\n}",
    "    /// <summary>\n"
    "    /// Stops tracking the entity (Detached). Used after concurrency reconciliation so\n"
    "    /// a stale losing entity is never re-saved by a later SaveChanges in the same scope.\n"
    "    /// </summary>\n"
    "    void DetachTrackedEntity(object entity);\n"
    "\n"
    "    /// <summary>Reloads the entity's stored values from the database, discarding local changes.</summary>\n"
    "    Task ReloadEntityAsync(object entity, CancellationToken cancellationToken = default);\n"
    "\n"
    "    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);\n"
    "}",
)
patch(
    "src/MealMate.Persistence/AppDbContext.cs",
    "    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)",
    "    /// <inheritdoc />\n"
    "    public void DetachTrackedEntity(object entity) => Entry(entity).State = EntityState.Detached;\n"
    "\n"
    "    /// <inheritdoc />\n"
    "    public async Task ReloadEntityAsync(object entity, CancellationToken cancellationToken = default) =>\n"
    "        await Entry(entity).ReloadAsync(cancellationToken);\n"
    "\n"
    "    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)",
)

# ── 5. Voice commands: markers, V04 guards, V03 busy release, v1 reconcile ──
cmd_dir = "src/MealMate.Application/Features/VoiceCalls/Commands/"
guard_all = (
    "\n        // V04: legacy mutations serve legacy sessions only - a ProtocolVersion=2 call\n"
    "        // is device-bound and must be controlled through the v2 surface.\n"
    "        if (session.ProtocolVersion == 2)\n"
    "        {\n"
    "            return VoiceCallErrors.V2DeviceControlRequired();\n"
    "        }\n"
)
guard_nonsystem = (
    "\n        // V04: legacy mutations serve legacy sessions only. System recovery paths\n"
    "        // (IsSystem) stay protocol-agnostic so timeouts can still terminate v2 calls.\n"
    "        if (session.ProtocolVersion == 2 && !request.IsSystem)\n"
    "        {\n"
    "            return VoiceCallErrors.V2DeviceControlRequired();\n"
    "        }\n"
)
release = (
    "\n        // V03: terminal mutation, Busy release and lifecycle commit as one unit -\n"
    "        // a terminal call must never leave its participants permanently reserved.\n"
    "        var busySlots = await _db.VoiceCallBusyParticipants\n"
    "            .Where(b => b.CallId == session.Id)\n"
    "            .ToListAsync(cancellationToken);\n"
    "        if (busySlots.Count > 0)\n"
    "        {\n"
    "            _db.VoiceCallBusyParticipants.RemoveRange(busySlots);\n"
    "        }\n"
)

all_cmds = ["AnswerCallCommand.cs", "RejectCallCommand.cs", "CancelCallCommand.cs", "EndCallCommand.cs",
            "MarkCallConnectedCommand.cs", "MarkCallConnectingCommand.cs", "MarkCallFailedCommand.cs",
            "MarkCallMissedCommand.cs", "InitiateCallCommand.cs"]
for name in all_cmds:
    fp = cmd_dir + name
    s = io.open(fp, encoding="utf-8").read()
    changed = False
    # 5a. self-persisting marker
    if "ISelfPersistingCommand" not in s:
        s2 = re.sub(r"(: ICommand<[^>]+>(?:<[^>]+>)?)\s*;", r"\1, ISelfPersistingCommand;", s, count=1)
        if s2 == s:
            s2 = re.sub(r"(: ICommand<[^>]+>)\s*;", r"\1, ISelfPersistingCommand;", s, count=1)
        assert s2 != s, "marker anchor missing in " + name
        s = s2
        changed = True
    # 5b. V04 guard (after session-null check)
    if "V2DeviceControlRequired" not in s and name != "InitiateCallCommand.cs":
        guard = guard_nonsystem if name in ("MarkCallFailedCommand.cs", "EndCallCommand.cs") else guard_all
        m = re.search(r"(if \(session is null\)\n        \{\n            return VoiceCallErrors\.NotFound\(\);\n        \}\n)", s)
        assert m, "session-null anchor missing in " + name
        s = s[:m.end()] + guard + s[m.end():]
        changed = True
    # 5c. V03 busy release (terminal commands, before try/SaveChanges)
    if "busySlots" not in s and name in ("RejectCallCommand.cs", "CancelCallCommand.cs", "EndCallCommand.cs",
                                          "MarkCallMissedCommand.cs", "MarkCallFailedCommand.cs"):
        m = re.search(r"(\n        try\n        \{\n            await _db\.SaveChangesAsync\(cancellationToken\);)", s)
        assert m, "save anchor missing in " + name
        s = s[:m.start()] + release + s[m.start():]
        changed = True
    if changed:
        io.open(fp, "w", encoding="utf-8", newline="\n").write(s)
        print("re-fixed", name)

# 5d. error factory
p = cmd_dir + "../Errors/VoiceCallErrors.cs"
s = io.open(p, encoding="utf-8").read()
if "V2DeviceControlRequired" not in s:
    patch(p,
          "    public static Error ConcurrencyConflict() =>",
          "    public static Error V2DeviceControlRequired() =>\n"
          "        Error.Conflict(\"calls.v2_device_control_required\",\n"
          "            \"This call uses the v2 protocol and must be controlled through the v2 API with a device session.\");\n"
          "\n"
          "    public static Error ConcurrencyConflict() =>")

# 5e. v1 initiate: V02 participants-changed + V05 reconcile cleanup
p = cmd_dir + "InitiateCallCommand.cs"
s = io.open(p, encoding="utf-8").read()
if "ParticipantsChanged" not in s:
    old = ("                customerUserId = lockedParticipants.Value.CustomerUserId;\n"
           "                driverUserId = lockedParticipants.Value.DriverUserId;\n")
    if old in s:
        new = ("                // V02: the locked values are consumed only when they MATCH the pair the\n"
               "                // locks were acquired for. A changed pair means the locks guard the\n"
               "                // wrong users - retriable conflict, never creation with stale identities.\n"
               "                if (lockedParticipants.Value.CustomerUserId != customerUserId\n"
               "                    || lockedParticipants.Value.DriverUserId != driverUserId)\n"
               "                {\n"
               "                    return ParticipantsChanged();\n"
               "                }\n")
        s = s.replace(old, new, 1)
        print("v1 V02 re-applied")
    s = s.replace(
        "    private static Result<InitiateCallResponseDto> LegacyInitiationDisabled() =>",
        "    private static Result<CallSession> ParticipantsChanged() =>\n"
        "        Result.Failure<CallSession>(\n"
        "            Error.Conflict(\"calls.participants_changed\",\n"
        "                \"The delivery participants changed while the call was being created. Retry the request.\"));\n"
        "\n"
        "    private static Result<InitiateCallResponseDto> LegacyInitiationDisabled() =>", 1)
io.open(p, "w", encoding="utf-8", newline="\n").write(s)

# 5f. reconcile detach in v1 initiate (V05-safe) — restore DetachTrackedEntity usage
s = io.open(p, encoding="utf-8").read()
if "_db.DetachTrackedEntity(session)" not in s:
    old = ("            reconcileOnUniqueViolationAsync: async ct =>\n"
           "            {\n"
           "                // A concurrent identical request committed first (unique index on\n"
           "                // CallerUserId + ClientRequestId). Detach our failed staged insert\n")
    if old in s:
        print("v1 reconcile comment block present; ensure detach lines exist after it")
    # find the reconcile body and add detaches if missing
    if "DetachTrackedEntity" not in s:
        m = re.search(r"(reconcileOnUniqueViolationAsync: async ct =>\n            \{\n)", s)
        assert m, "v1 reconcile anchor missing"
        inject = ("                // V07: every staged entity must leave the tracker on reconciliation.\n"
                  "                if (session is not null) _db.DetachTrackedEntity(session);\n"
                  "                if (auditLog is not null) _db.DetachTrackedEntity(auditLog);\n\n")
        s = s[:m.end()] + inject + s[m.end():]
        io.open(p, "w", encoding="utf-8", newline="\n").write(s)
        print("v1 reconcile detaches re-applied")

# 5g. terminal commands: reconcile DetachTrackedEntity (Answer/Reject/Cancel/End/Mark*)
for name in ["AnswerCallCommand.cs", "RejectCallCommand.cs", "CancelCallCommand.cs", "EndCallCommand.cs",
             "MarkCallConnectedCommand.cs", "MarkCallConnectingCommand.cs", "MarkCallFailedCommand.cs",
             "MarkCallMissedCommand.cs"]:
    fp = cmd_dir + name
    s = io.open(fp, encoding="utf-8").read()
    if "DetachTrackedEntity" in s:
        continue
    m = re.search(r"(catch \(DbUpdateConcurrencyException\)\n        \{\n)", s)
    if not m:
        print("!! no concurrency catch in", name)
        continue
    inject = ("            // F04/V07: the losing entity must leave the tracker before reconciliation,\n"
              "            // or the pipeline/next batch command re-saves the stale row and crashes.\n"
              "            _db.DetachTrackedEntity(session);\n\n")
    s = s[:m.end()] + inject + s[m.end():]
    io.open(fp, "w", encoding="utf-8", newline="\n").write(s)
    print("re-detach", name)

# ── 6. Hub guards ──
p = "src/MealMate.Api/Realtime/VoiceCallHub.cs"
s = io.open(p, encoding="utf-8").read()
if "uses the v2 protocol" not in s:
    guard = ("        if (session.ProtocolVersion == 2)\n"
             "            throw new HubException(\"upgrade_required: this call uses the v2 protocol; signaling requires the v2 Hub with a device session.\");\n\n")
    pattern = re.compile(r"(        if \(session is null\)\n            throw new HubException\(\"Call session not found\.\"\);\n\n)")
    s2, n = pattern.subn(lambda m: m.group(1) + guard, s)
    assert n >= 1, "hub anchor missing"
    io.open(p, "w", encoding="utf-8", newline="\n").write(s2)
    print("hub guards re-applied:", n)

# ── 7. Location accuracy pipeline (G03) ──
p = "src/MealMate.Application/Features/DriverDelivery/Dispatcher/UpdateDriverLocationCommand.cs"
s = io.open(p, encoding="utf-8").read()
if "CapturedAtUtc" not in s:
    patch(p,
          "    double? AccuracyMeters = null) : ICommand<UpdateDriverLocationResponse>;",
          "    double? AccuracyMeters = null,\n    DateTime? CapturedAtUtc = null) : ICommand<UpdateDriverLocationResponse>;")
    old_validate = ("        // Coordinate validation is range-based: (0,0) is a geographically valid point\n"
                    "        // and only out-of-range or non-finite values are rejected.")
    new_validate = ("        // G03: measured accuracy is REQUIRED - an update without it is rejected so a\n"
                    "        // null-accuracy point can never displace the latest eligibility fix.\n"
                    "        if (!request.AccuracyMeters.HasValue\n"
                    "            || !double.IsFinite(request.AccuracyMeters.Value)\n"
                    "            || request.AccuracyMeters.Value <= 0\n"
                    "            || request.AccuracyMeters.Value > 10_000)\n"
                    "        {\n"
                    "            return Error.Validation(\n"
                    "                MessageCodes.Validation.InvalidFormat,\n"
                    "                _messages.GetMessage(MessageCodes.Validation.InvalidFormat,\n"
                    "                    \"accuracyMeters is required and must be within (0, 10000].\"));\n"
                    "        }\n"
                    "\n"
                    "        var capturedAt = request.CapturedAtUtc ?? DateTime.UtcNow;\n"
                    "        if (capturedAt > DateTime.UtcNow.AddMinutes(2))\n"
                    "        {\n"
                    "            return Error.Validation(\n"
                    "                MessageCodes.Validation.InvalidFormat,\n"
                    "                _messages.GetMessage(MessageCodes.Validation.InvalidFormat,\n"
                    "                    \"capturedAtUtc cannot be in the future.\"));\n"
                    "        }\n"
                    "\n"
                    "        // Coordinate validation is range-based: (0,0) is a geographically valid point\n"
                    "        // and only out-of-range or non-finite values are rejected.")
    patch(p, old_validate, new_validate)
    patch(p,
          "            // Server-authoritative ingest timestamps: client fix time is not trusted\n"
          "            // (replay guard); age decisions use captured+received.\n"
          "            CapturedAtUtc = now,",
          "            // Real client capture time (validated against replay above); ReceivedAtUtc\n"
          "            // stays server-authoritative ingest time for the captured+received age rule.\n"
          "            CapturedAtUtc = capturedAt,")
else:
    print("location command already fixed")

# DTO + controller forwarding
patch(
    "src/MealMate.Application/Features/DriverDelivery/Dispatcher/DispatcherDashboardDtos.cs",
    "    double? AccuracyMeters = null);",
    "    double? AccuracyMeters = null,\n    DateTime? CapturedAtUtc = null);",
    optional=True)
p = "src/MealMate.Api/Controllers/V1/DriverTrackingController.cs"
s = io.open(p, encoding="utf-8").read()
if "CapturedAtUtc: request.CapturedAtUtc" not in s:
    patch(p,
          "            AccuracyMeters: request.AccuracyMeters), cancellationToken);",
          "            AccuracyMeters: request.AccuracyMeters,\n            CapturedAtUtc: request.CapturedAtUtc), cancellationToken;")
p = "src/MealMate.Api/Controllers/V1/DispatcherDashboardController.cs"
s = io.open(p, encoding="utf-8").read()
if "AccuracyMeters: request.AccuracyMeters" not in s:
    patch(p,
          "            Heading: request.Heading,\n            SpeedKmh: request.SpeedKmh), cancellationToken);",
          "            Heading: request.Heading,\n            SpeedKmh: request.SpeedKmh,\n            AccuracyMeters: request.AccuracyMeters,\n            CapturedAtUtc: request.CapturedAtUtc), cancellationToken;")

# Hub UpdateLocationV2
p = "src/MealMate.Api/Realtime/DriverHub.cs"
s = io.open(p, encoding="utf-8").read()
if "UpdateLocationV2" not in s:
    old = ("    public async Task<object> UpdateLocation(\n"
           "        decimal latitude,\n"
           "        decimal longitude,\n"
           "        double? heading = null,\n"
           "        double? speedKmh = null)\n"
           "    {\n"
           "        if (Context.User?.Identity?.IsAuthenticated != true)\n"
           "        {\n"
           "            throw new HubException(\"unauthorized: connect with ?access_token=<driver JWT>\");\n"
           "        }\n")
    new = ("    public async Task<object> UpdateLocation(\n"
           "        decimal latitude,\n"
           "        decimal longitude,\n"
           "        double? heading = null,\n"
           "        double? speedKmh = null)\n"
           "    {\n"
           "        if (Context.User?.Identity?.IsAuthenticated != true)\n"
           "        {\n"
           "            throw new HubException(\"unauthorized: connect with ?access_token=<driver JWT>\");\n"
           "        }\n"
           "\n"
           "        // G03: the legacy invocation cannot carry measured accuracy. Clients must\n"
           "        // move to UpdateLocationV2 - an explicit upgrade requirement, not a silent\n"
           "        // accuracy fabrication.\n"
           "        throw new HubException(\"upgrade_required: location streaming now requires measured accuracy; invoke UpdateLocationV2(latitude, longitude, accuracyMeters, capturedAtUtc, heading, speedKmh)\");\n"
           "    }\n"
           "\n"
           "    /// <summary>\n"
           "    /// v2 GPS streaming (G03): the client sends its MEASURED accuracy and the real fix\n"
           "    /// capture time. The command validates both; the server stamps receive time.\n"
           "    /// </summary>\n"
           "    public async Task<object> UpdateLocationV2(\n"
           "        decimal latitude,\n"
           "        decimal longitude,\n"
           "        double accuracyMeters,\n"
           "        DateTime capturedAtUtc,\n"
           "        double? heading = null,\n"
           "        double? speedKmh = null)\n"
           "    {\n"
           "        if (Context.User?.Identity?.IsAuthenticated != true)\n"
           "        {\n"
           "            throw new HubException(\"unauthorized: connect with ?access_token=<driver JWT>\");\n"
           "        }\n")
    patch(p, old, new)
    patch(p,
          "        var result = await _sender.Send(new UpdateDriverLocationCommand(\n"
          "            DriverId: null,\n"
          "            TripId: null,\n"
          "            Latitude: latitude,\n"
          "            Longitude: longitude,\n"
          "            Heading: heading,\n"
          "            SpeedKmh: speedKmh));",
          "        var result = await _sender.Send(new UpdateDriverLocationCommand(\n"
          "            DriverId: null,\n"
          "            TripId: null,\n"
          "            Latitude: latitude,\n"
          "            Longitude: longitude,\n"
          "            Heading: heading,\n"
          "            SpeedKmh: speedKmh,\n"
          "            AccuracyMeters: accuracyMeters,\n"
          "            CapturedAtUtc: capturedAtUtc));")

# DriverApp: ReloadEntityAsync call-site + arrival dto (check)
p = "src/MealMate.Application/Features/DriverDelivery/DriverApp/DriverDeliveryOrderCommands.cs"
s = io.open(p, encoding="utf-8").read()
if "_db.Entry(stop).ReloadAsync" in s:
    patch(p, "await _db.Entry(stop).ReloadAsync(cancellationToken);",
          "await _db.ReloadEntityAsync(stop, cancellationToken);")

print("ALL RE-FIXES APPLIED")
