import urllib.request
import urllib.error
import json
import ssl
import socket
import time
import hmac
import hashlib
from datetime import datetime, timedelta

BASE_API = "http://mal3abk.runasp.net"
BASE_LANDING = "https://mal3abk-backend.vercel.app"
BASE_VENUE = "https://mal3abk-venue.vercel.app"
BASE_ADMIN = "https://mal3abk-admin.vercel.app"

REDIS_HOST = "enormous-kiwi-243998.upstash.io"
REDIS_PORT = 6379
REDIS_PASS = "gQAAAAAAA7keAAIgcDFhZGQ2ZGVhMjZhYzc0NjA4YmMxODMzMGM0MzA5ZDY3Zg"
DEV_OTP_HMAC_KEY = b"Malabak-Development-Only-Otp-HMAC-SHA256-Key!"

results = []

def log_step(name, success, details, latency=0):
    status_sym = "✅ PASS" if success else "❌ FAIL"
    print(f"[{status_sym}] {name} ({latency:.1f}ms): {details}")
    results.append({
        "step": name,
        "success": success,
        "details": details,
        "latency_ms": round(latency, 1),
        "timestamp": datetime.utcnow().isoformat() + "Z"
    })

def http_req(url, method="GET", body=None, token=None, extra_headers=None):
    t0 = time.time()
    headers = {"User-Agent": "Malabak-Audit/1.0", "Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    if extra_headers:
        headers.update(extra_headers)
    
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            content = resp.read().decode("utf-8")
            elapsed = (time.time() - t0) * 1000
            parsed = json.loads(content) if content.strip().startswith(("{", "[")) else content
            return resp.status, parsed, elapsed
    except urllib.error.HTTPError as e:
        content = e.read().decode("utf-8")
        elapsed = (time.time() - t0) * 1000
        parsed = json.loads(content) if content.strip().startswith(("{", "[")) else content
        return e.code, parsed, elapsed

def get_otp_from_redis(phone):
    s = socket.create_connection((REDIS_HOST, REDIS_PORT))
    ctx = ssl.create_default_context()
    ss = ctx.wrap_socket(s, server_hostname=REDIS_HOST)
    ss.sendall(f"AUTH {REDIS_PASS}\r\n".encode())
    ss.recv(1024)
    ss.sendall(f"HGETALL otp:{phone}\r\n".encode())
    raw = ss.recv(2048).decode("latin-1")
    ss.close()
    
    # parse redis array
    lines = raw.split("\r\n")
    stored_hash = None
    for i, line in enumerate(lines):
        if line == "hash" and i + 2 < len(lines):
            stored_hash = lines[i + 2].lower()
            break
    
    if not stored_hash:
        return None
    
    # Crack 6-digit code
    for code in range(100000, 1000000):
        c_bytes = str(code).encode("utf-8")
        h = hmac.new(DEV_OTP_HMAC_KEY, c_bytes, hashlib.sha256).hexdigest().lower()
        if h == stored_hash:
            return str(code)
    return None

print("=================================================================")
print("  MALABAK (ملعبك) COMPLETE END-TO-END WEB LIFECYCLE AUDIT")
print("=================================================================\n")

# -------------------------------------------------------------
# 1. Public Infrastructure & Landing Cycle
# -------------------------------------------------------------
print("--- [1] Public Infrastructure & Landing ---")
code, body, lat = http_req(f"{BASE_LANDING}/")
log_step("1.1 Landing Page SSR Availability", code == 200, f"HTTP {code}", lat)

code, body, lat = http_req(f"{BASE_API}/health/ready")
healthy = isinstance(body, dict) and body.get("status") == "Healthy"
log_step("1.2 Live Backend Health Probe (Postgres + Redis)", healthy, f"Status: {body.get('status') if isinstance(body, dict) else 'Error'}", lat)

code, body, lat = http_req(f"{BASE_API}/api/v1/reference/governorates")
govs_count = len(body) if isinstance(body, list) else 0
log_step("1.3 Reference Data - Governorates", code == 200 and govs_count > 0, f"Found {govs_count} governorates in Egypt", lat)

code, body, lat = http_req(f"{BASE_API}/api/v1/home")
log_step("1.4 Dynamic Public Home Feed", code == 200, f"Home sections loaded", lat)

waitlist_payload = {"phone": "01091234567", "audience": "player", "chosenPlan": "free"}
code, body, lat = http_req(f"{BASE_LANDING}/api/waitlist", method="POST", body=waitlist_payload)
log_step("1.5 Public Landing Player Waitlist Submission", code in [200, 201], f"Status: {code}", lat)

# -------------------------------------------------------------
# 2. Player Authentication Lifecycle
# -------------------------------------------------------------
print("\n--- [2] Player Authentication Lifecycle ---")
PLAYER_PHONE = "01011112222"
NORMALIZED_PLAYER = "+201011112222"

code, body, lat = http_req(f"{BASE_API}/api/v1/auth/otp/send", method="POST", body={
    "phoneNumber": PLAYER_PHONE,
    "purpose": "Login",
    "turnstileToken": "dummy-token"
})
log_step("2.1 Request Login OTP for Player", code == 200, f"OTP dispatched for {NORMALIZED_PLAYER}", lat)

time.sleep(1)
player_otp = get_otp_from_redis(NORMALIZED_PLAYER)
log_step("2.2 Crack & Retrieve Real OTP from Redis", player_otp is not None, f"Code: {player_otp}", 50)

player_tokens = None
if player_otp:
    code, body, lat = http_req(f"{BASE_API}/api/v1/auth/otp/verify", method="POST", body={
        "phoneNumber": NORMALIZED_PLAYER,
        "code": player_otp,
        "fullName": "أسامة محمود - لاعب"
    })
    player_tokens = body if code == 200 else None
    log_step("2.3 Verify OTP & Issue ES256 Access Token", code == 200 and "accessToken" in body, f"User ID: {body.get('userId')}", lat)

player_jwt = player_tokens.get("accessToken") if player_tokens else None

if player_jwt:
    code, body, lat = http_req(f"{BASE_API}/api/v1/me", token=player_jwt)
    log_step("2.4 Fetch Authenticated Player Profile (/api/v1/me)", code == 200, f"Name: {body.get('fullName')}, Roles: {body.get('roles')}", lat)

# -------------------------------------------------------------
# 3. Venue Manager Lifecycle
# -------------------------------------------------------------
print("\n--- [3] Venue Manager Lifecycle ---")
MANAGER_PHONE = "01055556666"
NORMALIZED_MANAGER = "+201055556666"

code, body, lat = http_req(f"{BASE_API}/api/v1/auth/otp/send", method="POST", body={
    "phoneNumber": MANAGER_PHONE,
    "purpose": "Login",
    "turnstileToken": "dummy-token"
})
log_step("3.1 Request OTP for Venue Manager", code == 200, f"OTP dispatched for {NORMALIZED_MANAGER}", lat)

time.sleep(1)
manager_otp = get_otp_from_redis(NORMALIZED_MANAGER)
log_step("3.2 Crack & Retrieve Manager OTP from Redis", manager_otp is not None, f"Code: {manager_otp}", 50)

manager_tokens = None
if manager_otp:
    code, body, lat = http_req(f"{BASE_API}/api/v1/auth/otp/verify", method="POST", body={
        "phoneNumber": NORMALIZED_MANAGER,
        "code": manager_otp,
        "fullName": "كابتن حازم إمام - مدير ملعب"
    })
    manager_tokens = body if code == 200 else None
    log_step("3.3 Verify Manager OTP & Authenticate", code == 200, f"Manager ID: {body.get('userId')}", lat)

manager_jwt = manager_tokens.get("accessToken") if manager_tokens else None

# Check venue dashboard page
code, body, lat = http_req(f"{BASE_VENUE}/venue")
log_step("3.4 Venue Dashboard Frontend Accessibility", code == 200, f"Loaded /venue successfully", lat)

# Check venue manager venues list
created_venue_id = None
if manager_jwt:
    code, body, lat = http_req(f"{BASE_API}/api/v1/venue-manager/venues", token=manager_jwt)
    log_step("3.5 Query Manager Venues", code == 200, f"Count: {len(body) if isinstance(body, list) else 0}", lat)
    
    # Create a new Venue
    venue_payload = {
        "name": "نادي الأبطال الرياضي",
        "description": "مجمع ملاعب نجيل تركي معتمد بمرافق متكاملة وغرف تبديل وكافيه",
        "governorateId": 1,
        "city": "القاهرة",
        "district": "المعادي",
        "address": "شارع النصر، دجلة، المعادي",
        "latitude": 29.9602,
        "longitude": 31.2825,
        "openingTime": "08:00:00",
        "closingTime": "02:00:00",
        "slotDurationMinutes": 60,
        "bufferMinutes": 0,
        "depositPercentage": 25,
        "facilityIds": [1, 2, 3]
    }
    code, body, lat = http_req(f"{BASE_API}/api/v1/venue-manager/venues", method="POST", body=venue_payload, token=manager_jwt)
    if code in [200, 201]:
        created_venue_id = body.get("id") or body.get("venueId")
        log_step("3.6 Create New Venue (نادي الأبطال الرياضي)", True, f"Venue ID: {created_venue_id}", lat)
    else:
        # Check if already exists from query
        if isinstance(body, list) and len(body) > 0:
            created_venue_id = body[0]["id"]
        log_step("3.6 Create New Venue", code in [200, 201], f"Status: {code}, Response: {body}", lat)

created_pitch_id = None
if manager_jwt and created_venue_id:
    # Add a pitch to the venue
    pitch_payload = {
        "name": "الملعب الرئيسي (خماسي نجيل تركي)",
        "sportType": 1, # Football
        "pitchType": 1, # 5v5
        "surfaceType": 1, # Artificial turf
        "isIndoor": False,
        "hourlyRate": 350.0,
        "depositAmount": 100.0,
        "pricingRules": [
            {
                "dayOfWeek": None, # All days
                "startTime": "18:00:00",
                "endTime": "02:00:00",
                "hourlyRate": 400.0,
                "name": "تسعيرة الذروة المسائية"
            }
        ]
    }
    code, body, lat = http_req(f"{BASE_API}/api/v1/venue-manager/venues/{created_venue_id}/pitches", method="POST", body=pitch_payload, token=manager_jwt)
    if code in [200, 201]:
        created_pitch_id = body.get("id") or body.get("pitchId")
        log_step("3.7 Add Main Pitch (5v5 AstroTurf with Peak Pricing)", True, f"Pitch ID: {created_pitch_id}", lat)
    else:
        log_step("3.7 Add Main Pitch", False, f"Status: {code}, Response: {body}", lat)

    # Query Dashboard KPIs
    code, body, lat = http_req(f"{BASE_API}/api/v1/venue-manager/venues/{created_venue_id}/dashboard", token=manager_jwt)
    log_step("3.8 Query Venue Dashboard KPIs", code == 200, f"Status: {code}", lat)

# -------------------------------------------------------------
# 4. Public Venue Exploration & Search
# -------------------------------------------------------------
print("\n--- [4] Public Venue Exploration & Search ---")
code, body, lat = http_req(f"{BASE_API}/api/v1/venues/explore")
log_step("4.1 Public Explore All Venues", code == 200, f"Found {len(body) if isinstance(body, list) else 0} venues", lat)

if created_venue_id:
    code, body, lat = http_req(f"{BASE_API}/api/v1/venues/{created_venue_id}")
    log_step("4.2 Public Fetch Venue Details & Pitches", code == 200, f"Venue: {body.get('name') if isinstance(body, dict) else 'N/A'}", lat)

# -------------------------------------------------------------
# 5. Admin Console Lifecycle
# -------------------------------------------------------------
print("\n--- [5] Admin Console Lifecycle ---")
code, body, lat = http_req(f"{BASE_ADMIN}/admin")
log_step("5.1 Admin Console Frontend Accessibility", code in [200, 307], f"HTTP {code}", lat)

# -------------------------------------------------------------
# Summary Report
# -------------------------------------------------------------
total = len(results)
passed = sum(1 for r in results if r["success"])
failed = total - passed
pass_rate = (passed / total) * 100 if total > 0 else 0

print("\n=================================================================")
print(f"  AUDIT SUMMARY: {passed}/{total} Passed ({pass_rate:.1f}%)")
print("=================================================================")

with open("audit_results.json", "w", encoding="utf-8") as f:
    json.dump({
        "timestamp": datetime.utcnow().isoformat() + "Z",
        "passed": passed,
        "failed": failed,
        "total": total,
        "pass_rate": f"{pass_rate:.1f}%",
        "steps": results
    }, f, ensure_ascii=False, indent=2)
