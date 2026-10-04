"""Runner for restaurant-service-capacity-dry-run.sql (READ-ONLY).

Executes every SELECT section of the dry-run script against the configured
PostgreSQL database and prints the rows. Refuses any non-SELECT statement.

Usage (from backend/):
    python scripts/run-capacity-dry-run.py [--from 2026-10-03] [--to 2026-11-02] [--dsn "Host=..."]

The connection string defaults to the one in src/MealMate.Api/appsettings.Development.json.
"""

import argparse
import json
import re
import sys
from datetime import date, timedelta
from pathlib import Path

import psycopg2

BACKEND_DIR = Path(__file__).resolve().parent.parent
SQL_PATH = Path(__file__).resolve().parent / "restaurant-service-capacity-dry-run.sql"
APPSETTINGS = BACKEND_DIR / "src" / "MealMate.Api" / "appsettings.Development.json"

FORBIDDEN = re.compile(r"\s*(insert|update|delete|truncate|alter|create|drop|grant|copy)\b", re.IGNORECASE)


def load_dsn() -> str:
    with APPSETTINGS.open(encoding="utf-8-sig") as fh:
        settings = json.load(fh)
    return to_psycopg2_dsn(settings["ConnectionStrings"]["MealMateDb"])


def to_psycopg2_dsn(ado_dsn: str) -> str:
    """Convert an ADO.NET connection string to psycopg2 keyword=value form."""
    key_map = {"host": "host", "port": "port", "database": "dbname", "username": "user", "password": "password"}
    parts = []
    for pair in ado_dsn.split(";"):
        pair = pair.strip()
        if not pair or "=" not in pair:
            continue
        key, value = pair.split("=", 1)
        mapped = key_map.get(key.strip().lower())
        if mapped and value.strip():
            parts.append(f"{mapped}={value.strip()}")
    return " ".join(parts)


def split_statements(sql: str):
    """Split on semicolons at end of line; keep each section's leading comments."""
    for chunk in re.split(r";\s*\n", sql):
        body = chunk.strip()
        if not body:
            continue
        yield body


def section_title(chunk: str) -> str:
    titles = re.findall(r"--\s*(S\d+[a-z]?\.?.*)", chunk)
    return titles[0].strip() if titles else "(section)"


def substitute_dates(sql: str, from_date: date, to_date: date) -> str:
    return sql.replace(":'from_date'", f"'{from_date.isoformat()}'").replace(":'to_date'", f"'{to_date.isoformat()}'")


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the capacity reconciliation dry-run (read-only).")
    parser.add_argument("--from", dest="from_date", type=date.fromisoformat, default=date.today())
    parser.add_argument("--to", dest="to_date", type=date.fromisoformat, default=date.today() + timedelta(days=30))
    parser.add_argument("--dsn", default=None, help="PostgreSQL connection string (ADO.NET style)")
    args = parser.parse_args()

    dsn = to_psycopg2_dsn(args.dsn) if args.dsn else load_dsn()

    sql = substitute_dates(SQL_PATH.read_text(encoding="utf-8"), args.from_date, args.to_date)

    connection = psycopg2.connect(dsn)
    try:
        with connection.cursor() as cursor:
            cursor.execute("SELECT 1")
            print(f"# connected; window {args.from_date} .. {args.to_date}")
            for chunk in split_statements(sql):
                without_comments = re.sub(r"--[^\n]*", "", chunk)
                if not without_comments.strip():
                    continue
                if FORBIDDEN.match(without_comments):
                    raise SystemExit(f"Refusing non-SELECT statement: {section_title(chunk)}")
                title = section_title(chunk)
                cursor.execute(without_comments)
                if cursor.description is None:
                    continue
                columns = [desc.name for desc in cursor.description]
                rows = cursor.fetchall()
                print(f"\n## {title}  ({len(rows)} rows)")
                print(" | ".join(columns))
                for row in rows:
                    print(" | ".join("" if value is None else str(value) for value in row))
    finally:
        connection.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
