# Sending a watchface to the watch

[Project overview](../README.md) | [Documentation index](README.md)

This workflow requires the uploader configuration described in [Installation](installation.md).

1. Generate the face and check the build log.
2. Turn off Bluetooth on the phone temporarily so it releases the watch connection.
3. Keep the watch near the PC and choose **Send to watch**.
4. If the binary has not been generated or has become outdated, the editor asks for an output path and builds it before sending.
5. Accept a pairing request on the watch if one appears.
6. Wait for the installation result, then check the actual watch display.
7. Re-enable phone Bluetooth after the PC disconnects.

To install an existing export, choose **Send .bin to watch** in the toolbar or **Watch > Send .bin to watch...**, then select the file. This action reads the ID from the selected package and uploads it without building, saving or changing the open project. The uploader validates the package before connecting; invalid or incompatible exports are rejected. The original selected file is preserved; collision recovery may save a separate copy with another ID.

Uploads from the open project start with ID **253**; direct binary uploads start with the ID stored in that file. If that slot is occupied, it can try the other known custom IDs **251, 252 and 260** while preserving installed faces. These IDs were used in this project's experiments; they are not a universal reserved range. If every candidate is occupied, the uploader stops. It does not delete a working face automatically.

The first Windows pairing attempt may fail and disconnect, followed by a successful encryption-only reconnection. The reconnect pause is 0.5 seconds; an already dropped link does not receive a redundant disconnect request. Bluetooth discovery, Windows pairing and connection setup may still take longer. A completed chunk transfer is not by itself proof of installation: the uploader also waits for the watch to report the intended dial ID.
