# Troubleshooting

[Project overview](../README.md) | [Documentation index](README.md)

| Symptom | What to check |
| --- | --- |
| The editor cannot build or open | Confirm the .NET 9 SDK and rebuild the WPF project |
| Missing Python, compiler or image-converter error | Check `.venv`, optional `HAYLOU_PYTHON`/`HAYLOU_PROJECT_ROOT` overrides, `HAYLOU_ARM_GCC` and `watchface/tools/ezip.exe` |
| Upload script not found | Keep `watchface/send_face2.py` beside the builder and its helper modules |
| Watch address is not configured | Set `HAYLOU_WATCH_ADDRESS` before launching the editor; validation with `--check` does not need it |
| Watch not found or connection drops | Release the phone's Bluetooth connection, check the adapter and configured watch address, and keep the watch nearby |
| Pairing error followed by reconnection | The investigated Windows setup sometimes needs a second encryption-only attempt; inspect the final result |
| Occupied dial ID | Let the uploader try its known alternatives; if all are occupied, remove a face through the watch/app before trying again |
| Transfer completes but the wrong face remains | Check the final reported dial ID; chunk acknowledgments alone do not confirm installation |
| TBWF elements are missing or misplaced | Review visibility, supported sources, resource mappings, frame offsets and source aspect ratio |
| No `%` or temperature unit symbol | Add a separate text/image element; these sources provide numeric values |
| Temperature shows `--` | Synchronize weather from the phone and verify the watch date; no forecast for today is treated as unavailable |
| Preview succeeds but the watch falls back or crashes | Keep the generated binary and log, note the firmware and imported design, and investigate before retrying the same package |

The **Recovery** action is a factory reset. It removes watchfaces, pairing and settings, and is intended for recovery rather than routine installation.
