import os
import sys
import time
import ftplib
import io
import urllib.request
import urllib.error

HOST = os.environ.get("MONSTERASP_FTP_SERVER", "site103465.siteasp.net")
USER = os.environ.get("MONSTERASP_FTP_USERNAME", "site103465")
PASSWORD = os.environ.get("MONSTERASP_FTP_PASSWORD", "Js6!a7#ZXe2_")
LOCAL_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "publish"))
REMOTE_ROOT = "/wwwroot"

def get_ftp():
    ftp = ftplib.FTP(HOST, timeout=30)
    ftp.login(USER, PASSWORD)
    return ftp

def ensure_remote_dir(ftp, remote_dir):
    parts = [p for p in remote_dir.replace("\\", "/").split("/") if p]
    current = ""
    for part in parts:
        current += "/" + part
        try:
            ftp.cwd(current)
        except ftplib.error_perm:
            try:
                ftp.mkd(current)
            except ftplib.error_perm:
                pass

def main():
    print(f"==> Connecting to MonsterASP FTP {HOST}...")
    ftp = get_ftp()
    print("==> Connected successfully.")

    print("==> Stopping IIS process with app_offline.htm...")
    try:
        ftp.cwd(REMOTE_ROOT)
    except:
        pass
    ftp.storbinary("STOR app_offline.htm", io.BytesIO(b"<!DOCTYPE html><html><body><h1>Deploying updates to Malabak API...</h1></body></html>"))
    print("==> App offline page deployed. Waiting 4 seconds for IIS unlock...")
    time.sleep(4)

    files = []
    for root, _, filenames in os.walk(LOCAL_DIR):
        for f in filenames:
            lp = os.path.join(root, f)
            rp = os.path.relpath(lp, LOCAL_DIR).replace("\\", "/")
            # Skip OS-specific non-Windows runtimes and sensitive server config files
            if rp.startswith("runtimes/linux") or rp.startswith("runtimes/osx"):
                continue
            if rp == "web.config":
                continue
            if rp in ("appsettings.json", "appsettings.Development.json"):
                continue
            files.append((lp, rp))

    total = len(files)
    print(f"==> Uploading {total} files from {LOCAL_DIR}...")
    uploaded = 0
    skipped_count = 0

    # Get remote file size map to speed up upload for identical files
    remote_sizes = {}
    try:
        for item in ftp.nlst():
            try:
                remote_sizes[item] = ftp.size(item)
            except:
                pass
    except:
        pass

    for lp, rp in files:
        rdir = os.path.dirname(rp)
        rfile = os.path.basename(rp)
        local_size = os.path.getsize(lp)

        # Quick skip for identical root DLLs/files with same size
        if not rdir and rfile in remote_sizes and remote_sizes[rfile] == local_size and not rfile.startswith("Malabak."):
            skipped_count += 1
            uploaded += 1
            if uploaded % 20 == 0 or uploaded == total:
                print(f"    Progress: [{uploaded}/{total}] (skipped unchanged dependencies: {skipped_count})")
            continue

        for attempt in range(3):
            try:
                ftp.cwd(REMOTE_ROOT)
                if rdir:
                    ensure_remote_dir(ftp, rdir)
                    ftp.cwd(REMOTE_ROOT.rstrip("/") + "/" + rdir)
                with open(lp, "rb") as fh:
                    ftp.storbinary(f"STOR {rfile}", fh)
                uploaded += 1
                if uploaded % 20 == 0 or uploaded == total:
                    print(f"    Progress: [{uploaded}/{total}] (skipped unchanged: {skipped_count})")
                break
            except Exception as e:
                print(f"    [Retry {attempt+1}] {rp}: {e}")
                time.sleep(1)
                try:
                    ftp.quit()
                except:
                    pass
                ftp = get_ftp()

    print("==> Starting app by removing app_offline.htm...")
    try:
        ftp.cwd(REMOTE_ROOT)
        ftp.delete("app_offline.htm")
    except Exception as e:
        print(f"Notice on deleting app_offline: {e}")

    try:
        ftp.quit()
    except:
        pass

    print("==> Waiting 6 seconds for IIS worker boot...")
    time.sleep(6)

    print("==> Verifying Health at http://mal3abk.runasp.net/health/ready...")
    health_url = "http://mal3abk.runasp.net/health/ready"
    success = False
    for i in range(12):
        try:
            req = urllib.request.Request(health_url, headers={"User-Agent": "DeployScript/1.0"})
            with urllib.request.urlopen(req, timeout=15) as res:
                body = res.read().decode("utf-8")
                if res.status == 200:
                    print(f"==> Deployment Successful! Health: 200 OK\n{body}")
                    success = True
                    break
        except Exception as e:
            print(f"    Waiting for startup ({i+1}/12): {e}")
            time.sleep(4)

    if not success:
        print("==> Health verification timed out or failed.")
        sys.exit(1)
    else:
        print("\n🎉 Backend deployed successfully to MonsterASP.NET!")

if __name__ == "__main__":
    main()
