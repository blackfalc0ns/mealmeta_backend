"""Read only safe gateway diagnostics from the deployed application's SFTP files."""
import hashlib
import json
import os
import posixpath
import re
import stat
import xml.etree.ElementTree as ET
from urllib.parse import urlparse

import paramiko


def main():
    host = os.environ.get("FTP_SERVER", "").strip()
    if not host:
        address = os.environ.get("SERVER_COMPUTER_NAME", "").strip()
        host = urlparse(address if "://" in address else "https://" + address).hostname
    if not host:
        website = os.environ.get("WEBSITE_NAME", "").strip()
        host = website + ".siteasp.net" if website else None
    username = (os.environ.get("FTP_USERNAME") or os.environ.get("SERVER_USERNAME") or os.environ.get("WEBSITE_NAME") or "").strip()
    password = (os.environ.get("FTP_PASSWORD") or os.environ.get("SERVER_PASSWORD") or "").strip("\r\n")
    root = (os.environ.get("FTP_SERVER_DIR") or "/wwwroot/").strip("/")
    if not host or not username or not password:
        raise RuntimeError("Deployment credentials are unavailable")

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(host, port=22, username=username, password=password, timeout=25, banner_timeout=25, auth_timeout=25)
    try:
        with client.open_sftp() as sftp:
            # Resolve the site's directory in the same way as the deployment workflow.
            sftp.chdir(root)
            settings = {}
            for filename in ("appsettings.json", "appsettings.Production.json"):
                try:
                    with sftp.open(filename, "r") as source:
                        data = json.loads(source.read())
                    for section, values in data.items():
                        if isinstance(values, dict):
                            settings.setdefault(section, {}).update(values)
                except FileNotFoundError:
                    pass

            overrides = {}
            try:
                with sftp.open("web.config", "r") as source:
                    config = ET.fromstring(source.read())
                overrides = {item.get("name"): item.get("value", "") for item in config.findall(".//environmentVariable")}
            except FileNotFoundError:
                pass
            encryption_key = overrides.get("Email__SecretsEncryptionKey") or settings.get("Email", {}).get("SecretsEncryptionKey")
            print("DIAGNOSTIC encryption key visible in site files:", bool(encryption_key))
            expected_hash = os.environ.get("EXPECTED_KEY_SHA256", "")
            if encryption_key and expected_hash:
                import base64
                actual_hash = hashlib.sha256(base64.b64decode(encryption_key.strip(), validate=True)).hexdigest()
                print("DIAGNOSTIC site key matches database-update key:", actual_hash == expected_hash)

            connection = overrides.get("ConnectionStrings__MealMateDb") or settings.get("ConnectionStrings", {}).get("MealMateDb", "")
            host_match = re.search(r"(?:^|;)\s*Host=([^;]+)", connection, re.IGNORECASE)
            if host_match:
                print("DIAGNOSTIC database host in site files:", host_match.group(1).strip())

            files = [entry for entry in sftp.listdir_attr("logs") if stat.S_ISREG(entry.st_mode) and entry.filename.startswith("stdout")]
            pattern = re.compile(r"MyFatoorah InitiatePayment failed at (configuration|request|response parsing); host ([A-Za-z0-9.\-]*|null), HTTP status ([0-9]*|null), exception ([A-Za-z0-9_]+)")
            found = 0
            for entry in sorted(files, key=lambda item: item.st_mtime, reverse=True)[:3]:
                with sftp.open(posixpath.join("logs", entry.filename), "rb") as source:
                    source.seek(max(0, entry.st_size - 2 * 1024 * 1024))
                    tail = source.read().decode("utf-8", errors="replace")
                matches = list(pattern.finditer(tail))
                for match in matches[-10:]:
                    found += 1
                    print("DIAGNOSTIC", match.group(0))
                print("DIAGNOSTIC legacy-token decryption warnings in log tail:", tail.count("Using a legacy MyFatoorah API token; resave gateway settings to encrypt it."))
            print("DIAGNOSTIC matching gateway failures:", found)
    finally:
        client.close()


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # Never dump connection configuration or arbitrary application logs.
        print("DIAGNOSTIC read failed:", type(error).__name__)
        raise SystemExit(1)
