#!/usr/bin/env python3
"""Blocking backend family-flow localization and suspicious-data release checks.

This is a scoped lexical guard, not a proof that all runtime data is authentic.
It deliberately checks real production source roots used by family checkout,
subscription lifecycle, and customer calendar flows; it excludes tests/docs.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


BACKEND = Path(__file__).resolve().parents[1]
RESOURCE_EN = Path("src/MealMate.Api/Resources/Resources.Messages.resx")
RESOURCE_AR = Path("src/MealMate.Api/Resources/Resources.Messages.ar.resx")

# Explicit production scope. Directories are checked recursively so new family
# flow files cannot silently fall outside the gate. These are source ownership
# boundaries, not allowlists of findings.
SOURCE_ROOTS = (
    Path("src/MealMate.Application/Features/FamilyCheckout"),
    Path("src/MealMate.Application/Features/FamilyManager"),
    Path("src/MealMate.Application/Features/CustomerCalendar"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/CustomerSubscriptions"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/Freeze"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/Renewal"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/Upgrade"),
)
SOURCE_FILES = (
    # Family lifecycle handlers at the ProgramsSubscriptions root are outside
    # the narrower lifecycle directories below, so list them explicitly.
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/SubscriptionOperationsHandlers.cs"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/FamilyAndOffersHandlers.cs"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/FamilyCancellationSettlement.cs"),
    Path("src/MealMate.Application/Features/ProgramsSubscriptions/Cancellation/FamilyCancellationFeeAllocationService.cs"),
    Path("src/MealMate.Api/Controllers/V1/FamilySubscriptionController.cs"),
    Path("src/MealMate.Api/Controllers/V1/CustomerFamilyManagerController.cs"),
    Path("src/MealMate.Api/Controllers/V1/AdminFamilyGroupsController.cs"),
    Path("src/MealMate.Api/Controllers/V1/CustomerFamilySubscriptionFlowController.cs"),
    Path("src/MealMate.Api/Controllers/V1/CustomerCalendarController.cs"),
    Path("src/MealMate.Api/Controllers/V1/CustomerSubscriptionManagementController.cs"),
    Path("tools/FamilyFlowReconciliation/Program.cs"),
)

PLACEHOLDER_BODY_RE = re.compile(r"\s*([A-Za-z_][A-Za-z0-9_]*|\d+)\s*(?:,[^}:]*)?(?::[^}]*)?\s*\Z")
SUSPICIOUS_DATA_PATTERNS = (
    ("fake-business-label", re.compile(
        r"(?i)(?:\"[^\"\n]*(?:mock|fake|dummy|sample|demo|test\s*data|placeholder|john\s+doe|example\.com|lorem)[^\"\n]*\"|'[^'\n]*(?:mock|fake|dummy|sample|demo|test\s*data|placeholder|john\s+doe|example\.com|lorem)[^'\n]*')"
    )),
    ("fabricated-business-default", re.compile(
        r"(?im)^\s*(?:public|private|internal|protected)\s+(?:static\s+)?(?:decimal|double|float|int|long)\s+\w*(?:price|amount|revenue|total|count|customers|orders|stock)\w*\s*=\s*\d+(?:\.\d+)?m?\s*;"
    )),
    ("fabricated-success-fallback", re.compile(
        r"(?is)catch\s*(?:\([^)]*\))?\s*\{[^{}]{0,500}return\s+(?:Task\.FromResult\s*\()?\s*(?:Result\.Success|Success\s*\(|new\s+\w*(?:Response|Dto)\s*\{)"
    )),
)


def load_resource(path: Path) -> tuple[dict[str, str], list[str]]:
    root = ET.parse(path).getroot()
    values: dict[str, str] = {}
    duplicates: list[str] = []
    for node in root.findall("data"):
        key = node.get("name", "")
        value = node.findtext("value", default="")
        if key in values:
            duplicates.append(key)
        values[key] = value
    return values, duplicates


def check_resources(en_path: Path, ar_path: Path) -> tuple[list[str], int]:
    failures: list[str] = []
    en, en_dupes = load_resource(en_path)
    ar, ar_dupes = load_resource(ar_path)
    if en_dupes:
        failures.append(f"English resource has duplicate keys: {', '.join(sorted(set(en_dupes)))}")
    if ar_dupes:
        failures.append(f"Arabic resource has duplicate keys: {', '.join(sorted(set(ar_dupes)))}")
    for key in sorted(en.keys() - ar.keys()):
        failures.append(f"Arabic resource is missing key: {key}")
    for key in sorted(ar.keys() - en.keys()):
        failures.append(f"English resource is missing key: {key}")
    for key in sorted(en.keys() & ar.keys()):
        if not en[key].strip() or not ar[key].strip():
            failures.append(f"Resource value is empty: {key}")
        en_args = placeholder_arguments(en[key])
        ar_args = placeholder_arguments(ar[key])
        if en_args != ar_args:
            failures.append(
                f"Placeholder mismatch for {key}: en={sorted(en_args)} ar={sorted(ar_args)}"
            )
    if not en:
        failures.append("No resource keys were scanned")
    return failures, len(en)


def source_files(base: Path) -> list[Path]:
    found: set[Path] = set()
    for rel in SOURCE_ROOTS:
        path = base / rel
        if not path.is_dir():
            raise ValueError(f"Required production scope directory missing: {rel.as_posix()}")
        found.update(path.rglob("*.cs"))
    for rel in SOURCE_FILES:
        path = base / rel
        if not path.is_file():
            raise ValueError(f"Required production scope file missing: {rel.as_posix()}")
        found.add(path)
    return sorted(found)


def code_only(text: str) -> str:
    """Blank comments without altering strings, chars, or line positions."""
    chars = list(text)
    i = 0
    state = "code"
    raw_end = 0
    while i < len(text):
        if state == "line_comment":
            if text[i] in "\r\n":
                state = "code"
            else:
                chars[i] = " "
            i += 1
            continue
        if state == "block_comment":
            if text.startswith("*/", i):
                chars[i] = chars[i + 1] = " "
                i += 2
                state = "code"
            else:
                if text[i] not in "\r\n":
                    chars[i] = " "
                i += 1
            continue
        if state == "raw_string":
            if text.startswith('"' * raw_end, i):
                i += raw_end
                state = "code"
            else:
                i += 1
            continue
        if state in ("string", "verbatim_string", "char"):
            terminator = "'" if state == "char" else '"'
            if state == "verbatim_string" and text.startswith('""', i):
                i += 2
            elif text[i] == "\\" and state != "verbatim_string":
                i += min(2, len(text) - i)
            elif text[i] == terminator:
                i += 1
                state = "code"
            else:
                i += 1
            continue

        if text.startswith("//", i):
            chars[i] = chars[i + 1] = " "
            i += 2
            state = "line_comment"
        elif text.startswith("/*", i):
            chars[i] = chars[i + 1] = " "
            i += 2
            state = "block_comment"
        elif text[i] == '"':
            run = 1
            while i + run < len(text) and text[i + run] == '"':
                run += 1
            if run >= 3:
                raw_end = run
                state = "raw_string"
                i += run
            else:
                verbatim = i > 0 and text[i - 1] == "@"
                state = "verbatim_string" if verbatim else "string"
                i += 1
        elif text[i] == "'":
            state = "char"
            i += 1
        else:
            i += 1
    return "".join(chars)


def placeholder_arguments(value: str) -> set[str]:
    """Return composite-format arguments, ignoring escaped double braces."""
    result: set[str] = set()
    i = 0
    while i < len(value):
        if value.startswith("{{", i) or value.startswith("}}", i):
            i += 2
            continue
        if value[i] != "{":
            i += 1
            continue
        end = value.find("}", i + 1)
        if end < 0:
            i += 1
            continue
        body = value[i + 1:end]
        match = PLACEHOLDER_BODY_RE.fullmatch(body)
        if match:
            result.add(match.group(1))
        i = end + 1
    return result


def check_producers(files: list[Path]) -> list[str]:
    findings: list[str] = []
    for path in files:
        content = code_only(path.read_text(encoding="utf-8-sig"))
        for name, pattern in SUSPICIOUS_DATA_PATTERNS:
            for match in pattern.finditer(content):
                line = content.count("\n", 0, match.start()) + 1
                findings.append(f"{path.as_posix()}:{line}: {name}")
    return findings


def scope_is_empty(files: list[Path]) -> bool:
    """The production scan must always have at least one source file."""
    return not files


def self_test() -> list[str]:
    failures: list[str] = []
    cases = (
        ("missing resource key", check_resources_from_text(
            '<root><data name="a"><value>Hi</value></data></root>',
            '<root></root>', True)),
        ("placeholder mismatch", check_resources_from_text(
            '<root><data name="a"><value>Hi {0}</value></data></root>',
            '<root><data name="a"><value>مرحبا {name}</value></data></root>', True)),
        ("duplicate resource key", check_resources_from_text(
            '<root><data name="a"><value>One</value></data><data name="a"><value>Two</value></data></root>',
            '<root><data name="a"><value>واحد</value></data></root>', True)),
        ("empty resource value", check_resources_from_text(
            '<root><data name="a"><value></value></data></root>',
            '<root><data name="a"><value>فارغ</value></data></root>', True)),
        ("empty resource scope", check_resources_from_text(
            '<root></root>', '<root></root>', True)),
        ("escaped brace placeholders", check_resources_from_text(
            '<root><data name="a"><value>Literal {{0}} and {0:N2}</value></data></root>',
            '<root><data name="a"><value>نص {{0}} و {0:N2}</value></data></root>', False)),
    )
    for label, detected in cases:
        if not detected:
            failures.append(f"Self-test did not detect {label}")

    bad = 'var customer = new CustomerDto { Name = "Demo customer" };'
    good = 'var id = Guid.NewGuid(); var items = new List<OrderDto>();'
    if not check_producer_text(bad):
        failures.append("Self-test did not detect fabricated business producer")
    embedded = 'var customer = new CustomerDto { Name = "/* Demo customer */" };'
    if not check_producer_text(embedded):
        failures.append("Self-test missed fabricated text inside comment-like string content")
    marker_before_literal = 'var note = "/*"; var customer = new CustomerDto { Name = "Demo customer" };'
    if not check_producer_text(marker_before_literal):
        failures.append("Self-test let a block-comment marker inside a string suppress a suspicious literal")
    if check_producer_text(good):
        failures.append("Self-test rejected permitted technical ID or empty collection")
    comment_only = '// "Demo customer"\n/* "Mock order" */\nvar id = Guid.NewGuid();'
    if check_producer_text(comment_only):
        failures.append("Self-test treated comment-only examples as production data")
    if not scope_is_empty([]):
        failures.append("Self-test allowed an empty production scan scope")
    with tempfile.TemporaryDirectory() as tmp:
        try:
            source_files(Path(tmp))
        except ValueError:
            pass
        else:
            failures.append("Self-test allowed missing/empty production scope")
    return failures


def check_resources_from_text(en_text: str, ar_text: str, should_fail: bool) -> bool:
    from tempfile import TemporaryDirectory

    with TemporaryDirectory() as tmp:
        en_path, ar_path = Path(tmp) / "en.resx", Path(tmp) / "ar.resx"
        en_path.write_text(en_text, encoding="utf-8")
        ar_path.write_text(ar_text, encoding="utf-8")
        failed, _ = check_resources(en_path, ar_path)
    return bool(failed) == should_fail


def check_producer_text(text: str) -> list[str]:
    cleaned = code_only(text)
    return [name for name, pattern in SUSPICIOUS_DATA_PATTERNS if pattern.search(cleaned)]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--root", type=Path, default=BACKEND,
                        help="backend root (defaults to this script's parent backend directory)")
    args = parser.parse_args()
    if args.self_test:
        errors = self_test()
        if errors:
            print("SELF-TEST FAIL")
            for error in errors:
                print(f"- {error}")
            return 1
        print("SELF-TEST PASS: invalid key, placeholder mismatch, fake producer, technical GUID and empty collection")
        return 0

    root = args.root.resolve()
    errors: list[str] = []
    try:
        resource_errors, key_count = check_resources(root / RESOURCE_EN, root / RESOURCE_AR)
        files = source_files(root)
        producer_errors = check_producers(files)
        errors.extend(resource_errors)
        errors.extend(producer_errors)
    except (OSError, ValueError, ET.ParseError) as exc:
        print(f"GATE ERROR: {exc}")
        return 1
    if key_count <= 0 or scope_is_empty(files):
        errors.append("Gate scope was empty (resource keys and production files must be nonzero)")
    print(f"Resource keys scanned: {key_count}")
    print(f"Scoped production C# files scanned: {len(files)}")
    print(f"Suspicious producer findings: {len(producer_errors)}")
    if errors:
        print("FAMILY RELEASE GATE FAIL")
        for error in errors:
            print(f"- {error}")
        return 1
    print("FAMILY RELEASE GATE PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
