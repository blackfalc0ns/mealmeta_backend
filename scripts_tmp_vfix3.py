import io
import re

guard_all = (
    "\n        // V04: legacy mutations serve legacy sessions only — a ProtocolVersion=2 call\n"
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

files_guard_all = ["AnswerCallCommand.cs", "RejectCallCommand.cs", "CancelCallCommand.cs",
                   "MarkCallConnectingCommand.cs", "MarkCallConnectedCommand.cs"]
files_guard_nonsystem = ["MarkCallFailedCommand.cs", "EndCallCommand.cs"]

for name in files_guard_all + files_guard_nonsystem:
    fp = f"src/MealMate.Application/Features/VoiceCalls/Commands/{name}"
    s = io.open(fp, encoding="utf-8").read()
    if "V2DeviceControlRequired" in s:
        print("already guarded", name)
        continue
    guard = guard_nonsystem if name in files_guard_nonsystem else guard_all
    pattern = re.compile(
        r"(if \(session is null\)\n        \{\n            return VoiceCallErrors\.NotFound\(\);\n        \}\n)")
    m = pattern.search(s)
    if m is None:
        print(f"!! no anchor in {name}")
        continue
    s = s[:m.end()] + guard + s[m.end():]
    io.open(fp, encoding="utf-8", newline="\n").write(s)
    print("guarded", name)

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
    if "busySlots" in s:
        print("already released", name)
        continue
    m = re.search(r"(\n        try\n        \{\n            await _db\.SaveChangesAsync\(cancellationToken\);)", s)
    if m is None:
        print(f"!! no save anchor in {name}")
        continue
    s = s[:m.start()] + release + s[m.start():]
    io.open(fp, encoding="utf-8", newline="\n").write(s)
    print("busy-release", name)

print("done")
