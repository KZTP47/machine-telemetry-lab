import json
import sys
import time
from pathlib import Path
from urllib.request import urlopen


def readings_from(path):
    return [json.loads(line) for line in Path(path).read_text().splitlines()]


def get(path):
    with urlopen(path, timeout=4) as response:
        return json.load(response)


simulated = readings_from(sys.argv[1])
sensor = readings_from(sys.argv[2]) if len(sys.argv) == 3 else []
assert [item["temperatureC"] for item in simulated] == [62, 75, 86, 81]
expected = {r["id"]: r for r in simulated + sensor}
thresholds = {r["id"]: 75 for r in simulated if r["temperatureC"] > 75}
if sensor:
    assert [item["temperatureC"] for item in sensor] == [30.125]
    thresholds[sensor[0]["id"]] = 28

deadline = time.monotonic() + 20
while True:
    readings = get("http://localhost:5181/readings")
    alerts = get("http://localhost:5182/alerts")
    found = {r["id"]: r for r in readings if r["id"] in expected}
    flagged = {a["reading"]["id"]: a["thresholdC"]
               for a in alerts if a["reading"]["id"] in expected}
    if found.keys() == expected.keys() and flagged == thresholds:
        break
    if time.monotonic() > deadline:
        raise SystemExit("The APIs did not return the expected readings and alert thresholds.")
    time.sleep(0.5)

for event_id, item in expected.items():
    assert found[event_id]["machineId"] == item["machineId"]
    assert found[event_id]["temperatureC"] == item["temperatureC"]
assert sum(r["id"] in expected for r in readings) == len(expected)
assert sum(a["reading"]["id"] in expected for a in alerts) == len(thresholds)
if sensor:
    print("PASS: external simulator and sensor file reached both APIs; each alert kept its original threshold.")
else:
    print("PASS: the external simulator reached both APIs.")
