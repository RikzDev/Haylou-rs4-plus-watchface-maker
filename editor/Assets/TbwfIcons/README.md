# Bundled TBWF icon resources

These 181 Android drawable definitions were extracted from the user's local
Hello Haylou APK with `watchface/tbwf_icon_library.py`. They preserve the resource
names referenced by TBWF files and are embedded in the editor assembly at build
time. Importing or using the bundled library does not require an APK, Python,
an Android installation, network access, or a previously populated cache.

The drawings originate from Hello Haylou and its underlying icon collections;
this extraction does not assign them a new license. The original APK and local
cache metadata are not included here.

Vectors and animation lists render as editable PNG assets. The two Android
layer-list circle effects are currently unsupported and are excluded from the
gallery with explicit warnings. Their definitions are retained for future
renderer support.

Image tinting keeps separate original frames in saved projects. Exported PNGs
contain the selected color, so the builder and watch need no tinting API.
