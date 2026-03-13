import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional

ROOT = Path(__file__).resolve().parents[1]
GAME_DATA = ROOT / "game" / "data"
SNAPSHOT_OUT = GAME_DATA / "aquaculture_snapshot.json"
REGIONS_OUT = GAME_DATA / "aquaculture_regions.json"
LOG_DIR = ROOT / "logs"
LOG_DIR.mkdir(parents=True, exist_ok=True)

BW_BASE = os.getenv("BARENTSWATCH_BASE_URL", "https://www.barentswatch.no/bwapi")
FDIR_BASE = os.getenv("FISKERIDIR_BASE_URL", "https://api.fiskeridir.no")
SSB_BASE = os.getenv("SSB_BASE_URL", "https://data.ssb.no/api/v0")
USER_AGENT = "LakskongenDataIntegration/1.0 (+offline snapshot builder)"


@dataclass
class RegionProfile:
    regionId: str
    regionName: str
    biomassUtilization: float
    licePressure: float
    diseasePressure: float
    escapeRisk: float
    environmentalCompliance: float
    temperatureAnomaly: float
    treatmentIntensity: float
    mortalityStress: float
    profitabilitySupport: float
    localityCount: int


class HttpClient:
    def __init__(self, timeout: int = 30):
        self.timeout = timeout

    def get_json(self, url: str, headers: Optional[Dict[str, str]] = None) -> Any:
        req = urllib.request.Request(url, headers=self._headers(headers), method="GET")
        return self._open_json(req)

    def post_json(self, url: str, payload: Dict[str, Any], headers: Optional[Dict[str, str]] = None) -> Any:
        body = json.dumps(payload).encode("utf-8")
        merged = self._headers(headers)
        merged["Content-Type"] = "application/json"
        req = urllib.request.Request(url, data=body, headers=merged, method="POST")
        return self._open_json(req)

    def _headers(self, headers: Optional[Dict[str, str]]) -> Dict[str, str]:
        base = {"User-Agent": USER_AGENT, "Accept": "application/json"}
        if headers:
            base.update(headers)
        return base

    def _open_json(self, req: urllib.request.Request) -> Any:
        with urllib.request.urlopen(req, timeout=self.timeout) as response:
            content = response.read().decode("utf-8")
            if not content.strip():
                return None
            return json.loads(content)


class BarentsWatchClient:
    def __init__(self, http: HttpClient, client_id: Optional[str], client_secret: Optional[str], audience: Optional[str]):
        self.http = http
        self.client_id = client_id
        self.client_secret = client_secret
        self.audience = audience
        self._token: Optional[str] = None

    def available(self) -> bool:
        return bool(self.client_id and self.client_secret)

    def latest_weekly_snapshot(self, year: int, week: int) -> Optional[Dict[str, Any]]:
        if not self.available():
            return None
        url = f"{BW_BASE}/v2/geodata/fishhealth/{year}/{week}"
        return self.http.post_json(url, payload={}, headers=self._auth_headers())

    def latest_localities(self, year: int, week: int) -> Optional[List[Dict[str, Any]]]:
        if not self.available():
            return None
        url = f"{BW_BASE}/v2/geodata/fishhealth/locality/{year}/{week}"
        data = self.http.post_json(url, payload={}, headers=self._auth_headers())
        return data if isinstance(data, list) else None

    def _auth_headers(self) -> Dict[str, str]:
        token = self._get_token()
        return {"Authorization": f"Bearer {token}"}

    def _get_token(self) -> str:
        if self._token:
            return self._token
        token_url = os.getenv("BARENTSWATCH_TOKEN_URL", "https://id.barentswatch.no/connect/token")
        payload = {
            "grant_type": "client_credentials",
            "client_id": self.client_id,
            "client_secret": self.client_secret,
        }
        if self.audience:
            payload["audience"] = self.audience
        encoded = urllib.parse.urlencode(payload).encode("utf-8")
        req = urllib.request.Request(
            token_url,
            data=encoded,
            headers={"Content-Type": "application/x-www-form-urlencoded", "User-Agent": USER_AGENT},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=self.http.timeout) as response:
            data = json.loads(response.read().decode("utf-8"))
        self._token = data["access_token"]
        return self._token


class FiskeridirClient:
    def __init__(self, http: HttpClient):
        self.http = http

    def available(self) -> bool:
        return True

    def get_json(self, path: str) -> Optional[Any]:
        url = f"{FDIR_BASE.rstrip('/')}/{path.lstrip('/')}"
        return self.http.get_json(url)


class SsbClient:
    def __init__(self, http: HttpClient):
        self.http = http

    def available(self) -> bool:
        return True

    def get_json(self, path: str) -> Optional[Any]:
        url = f"{SSB_BASE.rstrip('/')}/{path.lstrip('/')}"
        return self.http.get_json(url)


class SnapshotBuilder:
    def __init__(self, logger: List[str]):
        self.logger = logger

    def build(
        self,
        year: int,
        week: int,
        bw_weekly: Optional[Dict[str, Any]],
        bw_localities: Optional[List[Dict[str, Any]]],
        fiskeridir_payloads: Dict[str, Any],
        ssb_payloads: Dict[str, Any],
    ) -> Dict[str, Any]:
        regional_profiles = self._build_regional_profiles(bw_localities, fiskeridir_payloads)

        lice_pressure = self._safe_ratio(
            self._read_number(bw_weekly, "liceStatistics", "averageAdultFemaleLice"),
            fallback=0.28,
            min_value=0.0,
            max_value=0.8,
        )
        disease_pressure = self._bounded_index(
            self._read_number(bw_weekly, "weeklyDiseaseStatistics", "count"),
            baseline=12.0,
            floor=0.8,
            ceiling=1.8,
            fallback=1.05,
        )
        escape_risk = self._bounded_index(
            self._read_number(bw_weekly, "weeklyEscapeStatistics", "count"),
            baseline=1.0,
            floor=0.8,
            ceiling=1.8,
            fallback=1.0,
        )
        treatment_intensity = self._bounded_index(
            self._count_treatments(bw_localities),
            baseline=max(1, len(bw_localities or [])) * 0.35,
            floor=0.85,
            ceiling=1.6,
            fallback=1.02,
        )
        temperature_anomaly = self._build_temperature_anomaly(bw_localities)
        biomass_utilization = self._infer_biomass_utilization(fiskeridir_payloads)
        mortality_stress = self._infer_mortality_stress(fiskeridir_payloads)
        environmental_compliance = self._infer_environmental_compliance(fiskeridir_payloads)
        market_support = self._infer_market_support(ssb_payloads, biomass_utilization, mortality_stress)
        production_area_risk = self._infer_production_area_risk(regional_profiles)
        feed_sales_volume = self._infer_feed_sales_index(fiskeridir_payloads)
        profitability_index = max(0.85, min(1.2, market_support - ((mortality_stress - 1.0) * 0.25)))
        locality_crowding = self._infer_locality_crowding(regional_profiles)

        active_sources = []
        if bw_weekly or bw_localities:
            active_sources.append("barentswatch")
        if fiskeridir_payloads:
            active_sources.append("fiskeridirektoratet")
        if ssb_payloads:
            active_sources.append("ssb")

        freshness_days = 2 if bw_weekly else 7
        confidence = 0.55 + (0.15 if bw_weekly else 0.0) + (0.15 if fiskeridir_payloads else 0.0) + (0.10 if ssb_payloads else 0.0)
        confidence = round(min(0.98, confidence), 2)

        snapshot = {
            "retrievedAtUtc": datetime.now(timezone.utc).isoformat(),
            "sourceSummary": "Normalized from BarentsWatch Fishhealth, Fiskeridirektoratet public aquaculture data, and SSB market anchors",
            "snapshotYear": year,
            "snapshotWeek": week,
            "nationalBiomassUtilization": round(biomass_utilization, 3),
            "industryLicePressure": round(lice_pressure, 3),
            "productionAreaRiskIndex": round(production_area_risk, 3),
            "feedSalesVolumeIndex": round(feed_sales_volume, 3),
            "profitabilityIndex": round(profitability_index, 3),
            "diseasePressureIndex": round(disease_pressure, 3),
            "escapeRiskIndex": round(escape_risk, 3),
            "environmentalComplianceIndex": round(environmental_compliance, 3),
            "temperatureAnomalyIndex": round(temperature_anomaly, 3),
            "treatmentIntensityIndex": round(treatment_intensity, 3),
            "mortalityStressIndex": round(mortality_stress, 3),
            "localityCrowdingIndex": round(locality_crowding, 3),
            "marketPriceSupportIndex": round(market_support, 3),
            "activeSourceFlags": ",".join(active_sources),
            "regionalProfiles": [asdict(p) for p in regional_profiles],
            "sourceFreshnessDays": freshness_days,
            "dataConfidence": confidence,
        }

        self.logger.append(
            f"Built snapshot y={year} w={week} regions={len(regional_profiles)} lice={snapshot['industryLicePressure']} biomass={snapshot['nationalBiomassUtilization']} confidence={confidence}"
        )
        return snapshot

    def _build_regional_profiles(self, bw_localities: Optional[List[Dict[str, Any]]], fiskeridir_payloads: Dict[str, Any]) -> List[RegionProfile]:
        grouped: Dict[str, List[Dict[str, Any]]] = {}
        for locality in bw_localities or []:
            production_area = self._read_text(locality, "productionArea", "name") or self._read_text(locality, "productionArea") or "unknown"
            grouped.setdefault(production_area, []).append(locality)

        if not grouped:
            fallback_regions = [
                RegionProfile("PA03", "Production Area 3", 0.84, 0.27, 1.04, 1.00, 0.97, 1.02, 1.03, 1.01, 1.02, 24),
                RegionProfile("PA04", "Production Area 4", 0.88, 0.31, 1.10, 1.03, 0.95, 1.04, 1.08, 1.05, 1.03, 18),
                RegionProfile("PA06", "Production Area 6", 0.79, 0.22, 0.96, 0.99, 1.01, 0.99, 0.96, 0.97, 0.99, 20),
            ]
            self.logger.append("Using fallback regional profiles")
            return fallback_regions

        profiles: List[RegionProfile] = []
        for region_id, items in grouped.items():
            lice_values = [self._read_number(item, "adultFemaleLice", "average") for item in items]
            lice_values = [v for v in lice_values if v is not None]
            temp_values = [self._read_number(item, "seaTemperature") for item in items]
            temp_values = [v for v in temp_values if v is not None]
            disease_count = sum(len(item.get("diseases", []) or []) for item in items)
            treatment_count = sum(len(item.get("liceTreatments", []) or []) for item in items)
            fallow_count = sum(1 for item in items if item.get("isFallow"))
            escape_signals = sum(1 for item in items if item.get("farmedFishEscapes"))

            lice_pressure = sum(lice_values) / len(lice_values) if lice_values else 0.25
            temperature = sum(temp_values) / len(temp_values) if temp_values else 11.0
            temp_anomaly = self._clamp(1.0 + max(0.0, temperature - 12.0) * 0.04, 0.90, 1.20)
            disease_pressure = self._clamp(1.0 + (disease_count / max(1, len(items))) * 0.15, 0.85, 1.60)
            treatment_intensity = self._clamp(1.0 + (treatment_count / max(1, len(items))) * 0.10, 0.85, 1.60)
            escape_risk = self._clamp(1.0 + (escape_signals / max(1, len(items))) * 0.10, 0.90, 1.50)
            environmental_compliance = self._clamp(1.02 - (fallow_count / max(1, len(items))) * 0.05, 0.85, 1.05)
            biomass_utilization = self._clamp(0.78 + (len(items) * 0.01), 0.72, 0.94)
            mortality_stress = self._clamp(1.0 + max(0.0, disease_pressure - 1.0) * 0.5, 0.95, 1.40)
            profitability_support = self._clamp(1.0 + max(0.0, biomass_utilization - 0.82) * 0.25, 0.95, 1.08)

            profiles.append(
                RegionProfile(
                    regionId=region_id,
                    regionName=region_id,
                    biomassUtilization=round(biomass_utilization, 3),
                    licePressure=round(lice_pressure, 3),
                    diseasePressure=round(disease_pressure, 3),
                    escapeRisk=round(escape_risk, 3),
                    environmentalCompliance=round(environmental_compliance, 3),
                    temperatureAnomaly=round(temp_anomaly, 3),
                    treatmentIntensity=round(treatment_intensity, 3),
                    mortalityStress=round(mortality_stress, 3),
                    profitabilitySupport=round(profitability_support, 3),
                    localityCount=len(items),
                )
            )

        return sorted(profiles, key=lambda p: p.regionId)

    def _infer_biomass_utilization(self, fiskeridir_payloads: Dict[str, Any]) -> float:
        biomass = self._search_numeric(fiskeridir_payloads, ["biomass", "standingBiomass", "biomasse", "biomassKg"])
        mtb = self._search_numeric(fiskeridir_payloads, ["mtb", "maxBiomass", "capacity", "maks_tillatt_biomasse"])
        if biomass and mtb and mtb > 0:
            return self._clamp(biomass / mtb, 0.55, 0.98)
        return 0.84

    def _infer_feed_sales_index(self, fiskeridir_payloads: Dict[str, Any]) -> float:
        feed = self._search_numeric(fiskeridir_payloads, ["feed", "feedConsumption", "forforbruk", "feedKg"])
        if feed:
            return self._clamp(feed / 1000000.0, 0.90, 1.15)
        return 1.02

    def _infer_mortality_stress(self, fiskeridir_payloads: Dict[str, Any]) -> float:
        losses = self._search_numeric(fiskeridir_payloads, ["loss", "losses", "tap", "mortality"])
        stocked = self._search_numeric(fiskeridir_payloads, ["stocking", "stocked", "utsatt", "releasedFish"])
        if losses and stocked and stocked > 0:
            return self._clamp(1.0 + ((losses / stocked) * 8.0), 0.90, 1.50)
        return 1.04

    def _infer_environmental_compliance(self, fiskeridir_payloads: Dict[str, Any]) -> float:
        score = self._search_numeric(fiskeridir_payloads, ["environmentScore", "complianceScore", "momStatus", "environmentalStatus"])
        if score is not None:
            if score > 5.0:
                return self._clamp(score / 100.0, 0.75, 1.05)
            return self._clamp(score, 0.75, 1.05)
        return 0.97

    def _infer_market_support(self, ssb_payloads: Dict[str, Any], biomass_utilization: float, mortality_stress: float) -> float:
        export_price = self._search_numeric(ssb_payloads, ["price", "pricePerKg", "salmonPrice", "valuePerKg"])
        baseline = 82.0
        if export_price:
            support = 1.0 + ((export_price - baseline) / baseline) * 0.35
        else:
            support = 1.01
        support += max(0.0, biomass_utilization - 0.85) * 0.15
        support += max(0.0, mortality_stress - 1.0) * 0.10
        return self._clamp(support, 0.90, 1.20)

    def _infer_production_area_risk(self, profiles: List[RegionProfile]) -> float:
        if not profiles:
            return 1.0
        avg = sum(((p.licePressure * 0.5) + (p.diseasePressure * 0.5)) for p in profiles) / len(profiles)
        return self._clamp(avg, 0.90, 1.30)

    def _infer_locality_crowding(self, profiles: List[RegionProfile]) -> float:
        if not profiles:
            return 1.0
        avg = sum(p.biomassUtilization for p in profiles) / len(profiles)
        return self._clamp(0.9 + (avg * 0.2), 0.90, 1.20)

    def _build_temperature_anomaly(self, bw_localities: Optional[List[Dict[str, Any]]]) -> float:
        values = []
        for item in bw_localities or []:
            value = self._read_number(item, "seaTemperature")
            if value is not None:
                values.append(value)
        if not values:
            return 1.01
        avg = sum(values) / len(values)
        return self._clamp(1.0 + max(0.0, avg - 12.0) * 0.04, 0.90, 1.25)

    def _count_treatments(self, bw_localities: Optional[List[Dict[str, Any]]]) -> Optional[float]:
        if not bw_localities:
            return None
        count = 0
        for item in bw_localities:
            count += len(item.get("liceTreatments", []) or [])
        return float(count)

    def _read_number(self, payload: Optional[Dict[str, Any]], *path: str) -> Optional[float]:
        cursor: Any = payload
        for key in path:
            if not isinstance(cursor, dict) or key not in cursor:
                return None
            cursor = cursor[key]
        if isinstance(cursor, (int, float)):
            return float(cursor)
        return None

    def _read_text(self, payload: Optional[Dict[str, Any]], *path: str) -> Optional[str]:
        cursor: Any = payload
        for key in path:
            if not isinstance(cursor, dict) or key not in cursor:
                return None
            cursor = cursor[key]
        return cursor if isinstance(cursor, str) else None

    def _search_numeric(self, payload: Any, candidate_keys: List[str]) -> Optional[float]:
        if isinstance(payload, dict):
            for key, value in payload.items():
                if key in candidate_keys and isinstance(value, (int, float)):
                    return float(value)
                nested = self._search_numeric(value, candidate_keys)
                if nested is not None:
                    return nested
        elif isinstance(payload, list):
            for item in payload:
                nested = self._search_numeric(item, candidate_keys)
                if nested is not None:
                    return nested
        return None

    def _safe_ratio(self, value: Optional[float], fallback: float, min_value: float, max_value: float) -> float:
        if value is None:
            return fallback
        return self._clamp(value, min_value, max_value)

    def _bounded_index(self, value: Optional[float], baseline: float, floor: float, ceiling: float, fallback: float) -> float:
        if value is None or baseline <= 0:
            return fallback
        return self._clamp(value / baseline, floor, ceiling)

    def _clamp(self, value: float, minimum: float, maximum: float) -> float:
        return max(minimum, min(maximum, value))


def load_fixture(path: Optional[str]) -> Any:
    if not path:
        return None
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def latest_iso_week() -> tuple[int, int]:
    now = datetime.now(timezone.utc).isocalendar()
    return now.year, now.week


def fetch_optional(callable_fn, logger: List[str], label: str) -> Any:
    try:
        result = callable_fn()
        logger.append(f"Fetched {label}")
        return result
    except urllib.error.HTTPError as e:
        logger.append(f"{label} HTTP error {e.code}: {e.reason}")
    except urllib.error.URLError as e:
        logger.append(f"{label} URL error: {e.reason}")
    except Exception as e:
        logger.append(f"{label} failed: {e}")
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description="Fetch and normalize aquaculture snapshot data for Lakskongen")
    parser.add_argument("--year", type=int, help="ISO year to fetch")
    parser.add_argument("--week", type=int, help="ISO week to fetch")
    parser.add_argument("--bw-weekly-fixture", help="JSON fixture for BarentsWatch weekly aggregate")
    parser.add_argument("--bw-localities-fixture", help="JSON fixture for BarentsWatch locality data")
    parser.add_argument("--fdir-fixture", help="JSON fixture for Fiskeridirektoratet combined payload")
    parser.add_argument("--ssb-fixture", help="JSON fixture for SSB payload")
    parser.add_argument("--write-log", action="store_true", help="Write a timestamped integration log")
    args = parser.parse_args()

    year, week = latest_iso_week()
    year = args.year or year
    week = args.week or week

    logger: List[str] = []
    http = HttpClient(timeout=30)
    bw = BarentsWatchClient(
        http,
        client_id=os.getenv("BARENTSWATCH_CLIENT_ID"),
        client_secret=os.getenv("BARENTSWATCH_CLIENT_SECRET"),
        audience=os.getenv("BARENTSWATCH_AUDIENCE"),
    )
    fdir = FiskeridirClient(http)
    ssb = SsbClient(http)

    bw_weekly = load_fixture(args.bw_weekly_fixture)
    bw_localities = load_fixture(args.bw_localities_fixture)
    fiskeridir_payloads = load_fixture(args.fdir_fixture) or {}
    ssb_payloads = load_fixture(args.ssb_fixture) or {}

    if bw_weekly is None:
        bw_weekly = fetch_optional(lambda: bw.latest_weekly_snapshot(year, week), logger, "BarentsWatch weekly snapshot")
    if bw_localities is None:
        bw_localities = fetch_optional(lambda: bw.latest_localities(year, week), logger, "BarentsWatch locality snapshot")

    if not fiskeridir_payloads:
        fiskeridir_payloads = {
            "aquacultureRegister": fetch_optional(lambda: fdir.get_json("pub-aqua/"), logger, "Fiskeridirektoratet aquaculture register"),
            "environment": fetch_optional(lambda: fdir.get_json("envreportreg-public/"), logger, "Fiskeridirektoratet environment reports"),
        }
    if not ssb_payloads:
        ssb_payloads = {
            "market": fetch_optional(lambda: ssb.get_json("no/table/03013/"), logger, "SSB market series"),
        }

    builder = SnapshotBuilder(logger)
    snapshot = builder.build(year, week, bw_weekly, bw_localities, fiskeridir_payloads, ssb_payloads)

    GAME_DATA.mkdir(parents=True, exist_ok=True)
    with open(SNAPSHOT_OUT, "w", encoding="utf-8") as f:
        json.dump(snapshot, f, indent=2, ensure_ascii=False)

    with open(REGIONS_OUT, "w", encoding="utf-8") as f:
        json.dump(snapshot.get("regionalProfiles", []), f, indent=2, ensure_ascii=False)

    if args.write_log:
        stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        log_path = LOG_DIR / f"aquaculture-integration-{stamp}.log"
        with open(log_path, "w", encoding="utf-8") as f:
            f.write("\n".join(logger) + "\n")

    print(f"Wrote {SNAPSHOT_OUT}")
    print(f"Wrote {REGIONS_OUT}")
    for line in logger:
        print(f"- {line}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
