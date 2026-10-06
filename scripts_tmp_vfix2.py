import io
import re
import glob

# ─────────────────────────────────────────────────────────────
# V02 (v1): consume locked participants — conflict on pair change
# ─────────────────────────────────────────────────────────────
p = "src/MealMate.Application/Features/VoiceCalls/Commands/InitiateCallCommand.cs"
s = io.open(p, encoding="utf-8").read()

old = (
    "                customerUserId = lockedParticipants.Value.CustomerUserId;\n"
    "                driverUserId = lockedParticipants.Value.DriverUserId;\n"
)
new = (
    "                // V02: the locked values are consumed only when they MATCH the pair the\n"
    "                // locks were acquired for. A changed pair means the locks guard the\n"
    "                // wrong users — retriable conflict, never creation with stale identities.\n"
    "                if (lockedParticipants.Value.CustomerUserId != customerUserId\n"
    "                    || lockedParticipants.Value.DriverUserId != driverUserId)\n"
    "                {\n"
    "                    return Result.Failure<CallSession>(ParticipantsChanged());\n"
    "                }\n"
)
assert old in s, "v1 locked-assign anchor missing"
s = s.replace(old, new, 1)

s = s.replace(
    "    private static Result<InitiateCallResponseDto> LegacyInitiationDisabled() =>",
    "    private static Result<CallSession> ParticipantsChanged() =>\n"
    "        Result.Failure<CallSession>(\n"
    "            Error.Conflict(\"calls.participants_changed\",\n"
    "                \"The delivery participants changed while the call was being created. Retry the request.\"));\n"
    "\n"
    "    private static Result<InitiateCallResponseDto> LegacyInitiationDisabled() =>",
    1)
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("V02 v1 fixed")

# ─────────────────────────────────────────────────────────────
# V04: v1 client mutations refuse ProtocolVersion=2 calls
# ─────────────────────────────────────────────────────────────
error_factory = (
    "    public static Error V2DeviceControlRequired() =>\n"
    "        Error.Conflict(\"calls.v2_device_control_required\",\n"
    "            \"This call uses the v2 protocol and must be controlled through the v2 API with a device session.\");\n"
)
p = "src/MealMate.Application/Features/VoiceCalls/Errors/VoiceCallErrors.cs"
s = io.open(p, encoding="utf-8").read()
anchor = "    public static Error ConcurrencyConflict() =>"
assert anchor in s
s = s.replace(anchor, error_factory + "\n" + anchor, 1)
io.open(p, "w", encoding="utf-8", newline="\n").write(s)
print("error factory added")

guard_all = (
    "        // V04: legacy mutations serve legacy sessions only — a ProtocolVersion=2 call\n"
    "        // is device-bound and must be controlled through the v2 surface.\n"
    "        if (session.ProtocolVersion == 2)\n"
    "        {\n"
    "            return VoiceCallErrors.V2DeviceControlRequired();\n"
    "        }\n\n"
)
guard_nonsystem = (
    "        // V04: legacy mutations serve legacy sessions only. System recovery paths\n"
    "        // (IsSystem) stay protocol-agnostic so timeouts can still terminate v2 calls.\n"
    "        if (session.ProtocolVersion == 2 && !request.IsSystem)\n"
    "        {\n"
    "            return VoiceCallErrors.V2DeviceControlRequired();\n"
    "        }\n\n"
)

files_guard_all = ["AnswerCallCommand.cs", "RejectCallCommand.cs", "CancelCallCommand.cs",
                   "MarkCallConnectingCommand.cs", "MarkCallConnectedCommand.cs"]
files_guard_nonsystem = ["MarkCallFailedCommand.cs", "EndCallCommand.cs"]

for name in files_guard_all + files_guard_nonsystem:
    fp = f"src/MealMate.Application/Features/VoiceCalls/Commands/{name}"
    s = io.open(fp, encoding="utf-8").read()
    guard = guard_nonsystem if name in files_guard_nonsystem else guard_all
    # Insert right after the session-null check block.
    pattern = re.compile(
        r"(if \(session is null\)\n        \{\n            return VoiceCallErrors\.NotFound\(\);\n        \}\n)")
    m = pattern.search(s)
    if m is None:
        print(f"!! no anchor in {name}")
        continue
    s = s[:m.end()] + "\n" + guard + s[m.end():]
    io.open(fp, encoding="utf-8", newline="\n").write(s)
    print("guarded", name)

# MarkCallMissedCommand: recovery-only (no client route) — leave protocol-agnostic.

# ─────────────────────────────────────────────────────────────
# V03: Busy release inside every terminal command's save
# ─────────────────────────────────────────────────────────────
release = (
    "\n        // V03: terminal mutation, Busy release and lifecycle commit as one unit —\n"
    "        // a terminal call must never leave its participants permanently reserved.\n"
    "        var busySlots = await _db.VoiceCallBusyParticipants\n"
    "            .Where(b => b.CallId == session.Id)\n"
    "            .ToListAsync(cancellationToken);\n"
    "        if (busySlots.Count > 0)\n"
    "        {\n"
    "            _db.VoiceCallBusyParticipants.RemoveRange(busySlots);\n"
    "        }\n"
)

for name in ["RejectCallCommand.cs", "CancelCallCommand.cs", "EndCallCommand.cs",
             "MarkCallMissedCommand.cs", "MarkCallFailedCommand.cs"]:
    fp = f"src/MealMate.Application/Features/VoiceCalls/Commands/{name}"
    s = io.open(fp, encoding="utf-8").read()
    # insert immediately before the SaveChangesAsync that commits the transition
    pattern = re.compile(r"(\n\s*try\n        \{\n            await _db\.SaveChangesAsync\(cancellationToken\);)")
    m = pattern.search(s)
    if m is None:
        # some commands may call SaveChanges without try — find the transition path
        pattern2 = re.compile(r"(\n        (?:try\n        \{\n            )?await _db\.SaveChangesAsync\(cancellationToken\);)")
        m = pattern2.search(s)
    if m is None:
        print(f"!! no save anchor in {name}")
        continue
    s = s[:m.start()] + release + s[m.start():]
    io.open(fp, encoding="utf-8", newline="\n").write(s)
    print("busy-release", name)

print("done")
