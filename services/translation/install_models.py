"""Explicit provisioning only; runtime never downloads model packages."""
import argparse
import argostranslate.package

parser = argparse.ArgumentParser()
parser.add_argument("--sources", nargs="+", default=["en"])
args = parser.parse_args()
argostranslate.package.update_package_index()
packages = argostranslate.package.get_available_packages()
installed = {(p.from_code, p.to_code) for p in argostranslate.package.get_installed_packages()}
for source in args.sources:
    for origin, target in ([("en", "zh")] if source == "en" else [(source, "en"), ("en", "zh")]):
        if (origin, target) in installed:
            continue
        package = next((p for p in packages if p.from_code == origin and p.to_code == target), None)
        if package is None:
            raise SystemExit(f"No supported package: {origin}->{target}")
        argostranslate.package.install_from_path(package.download())
        installed.add((origin, target))
        print(f"Installed {origin}->{target} version={package.package_version}")
