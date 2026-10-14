"""Resolve occupied custom slots without deleting a working watchface."""

from build_live import retarget_live_package

# IDs previously used successfully by custom modules on this LS11.
# Slot 254 is reserved for the firmware's photo face.
CUSTOM_SLOTS = (253, 251, 252, 260)


async def resolve_collision(data, ident, error, announce, report):
    attempted = {ident}
    while error == 9:
        if ident not in CUSTOM_SLOTS:
            raise RuntimeError(f"Face {ident} already exists. The installed face was preserved.")
        next_ident = next((slot for slot in CUSTOM_SLOTS if slot not in attempted), None)
        if next_ident is None:
            raise RuntimeError(
                "All known custom slots are occupied. "
                "Remove a face using the watch or phone app before uploading another."
            )
        previous = ident
        data = retarget_live_package(data, next_ident)
        ident = next_ident
        attempted.add(ident)
        report(previous, ident, data)
        error = await announce(data, ident)
    return error, data, ident
