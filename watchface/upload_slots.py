"""Resolve occupied custom slots without deleting a working watchface."""

from build_live import retarget_live_package

# IDs previously used successfully by custom modules on this LS11.
# Slot 254 is reserved for the firmware's photo face.
CUSTOM_SLOTS = (253, 251, 252, 260)


async def resolve_collision(data, ident, error, announce, report):
    attempted = {ident}
    while error == 9:
        if ident not in CUSTOM_SLOTS:
            raise RuntimeError(f"A face {ident} ja existe. A face instalada foi preservada.")
        next_ident = next((slot for slot in CUSTOM_SLOTS if slot not in attempted), None)
        if next_ident is None:
            raise RuntimeError(
                "Os slots customizados conhecidos estao ocupados. "
                "Remova uma face pelo relogio ou aplicativo antes de enviar outra."
            )
        previous = ident
        data = retarget_live_package(data, next_ident)
        ident = next_ident
        attempted.add(ident)
        report(previous, ident, data)
        error = await announce(data, ident)
    return error, data, ident
