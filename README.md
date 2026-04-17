# HAWorkflows

Workflow builder + deterministic runtime packaged as a Home Assistant add-on.

## Add-on Repository

Add this repository in Home Assistant Add-on Store:

`https://github.com/MaxMinsk/HAWorkflows`

Add-on folder:

- `addon/`

Repository metadata:

- `repository.yaml`

## CI / Build / Publish

GitHub Actions:

- `.github/workflows/ci.yml`  
  Typecheck/build for frontend + `dotnet build`.
- `.github/workflows/addon-image.yml`  
  Builds and publishes multi-arch image to GHCR:
  - `ghcr.io/maxminsk/haworkflows:<version-from-addon-config>`
  - `ghcr.io/maxminsk/haworkflows:latest`

## Version Update Flow

1. Bump `version` in `addon/config.yaml`.
2. Push to `main`.
3. Action `addon-image.yml` publishes image with the same version tag.
4. Home Assistant sees updated add-on version and can update.
