![image](https://raw.githubusercontent.com/timonix/OneWare.PslExtension/main/Icon.png)

### Get Started

# PSL Extension

## Overview
This extension integrates **PSL** as a simulator with OneWare Studio, enabling direct interaction with PSL simulations without additional configuration overhead.
Should support Windows, Linux and MacOS. But only tested on Windows.

## Requirements
SymbiYosys can run in Docker or natively from OSS CAD Suite. Choose in **Settings → Tools → PSL Extension → SBY Runner**.

**Docker** (default)
- A container runtime, installed and running. Currently tested with **Docker Desktop** and **Rancher Desktop**.
- The hdlc/formal image is downloaded automatically on the first run, which may take a while (about 1 GB).

**Native (OSS CAD Suite)**
- **OSS CAD Suite** from the package manager (Binaries).
- On Windows also **OSS CAD Suite, GHDL plugin** from the package manager (Binaries), in the same version as your
  OSS CAD Suite. The Windows OSS CAD Suite does not include GHDL, so the plugin is built for each version in
  [ghdl-yosys-plugin-windows-release](https://github.com/timonix/ghdl-yosys-plugin-windows-release).
  Linux and macOS OSS CAD Suite include GHDL, nothing else is needed there.
- If OSS CAD Suite is updated, update the plugin to the same version. Running SBY tells you when they don't match.

## Installation
1. Install the extension.
2. Docker: install a container runtime.
   Native: install OSS CAD Suite and, on Windows, the matching OSS CAD Suite, GHDL plugin. Then set the SBY Runner to Native.

## Usage
- Right-click a .vhd file → **PSL → Generate SBY file** creates a .sby file next to it with some default settings.
- **PSL → Generate → simulate SBY file** creates it and runs it right away.
- Right-click a .sby file → **SBY → Run SBY** runs an existing one.
- The PSL simulator also shows up in the simulator list.
- With Docker, launch your container runtime first if it is not already running.

## Notes
- The generated sby files are a starting point. You may have to edit paths or settings.
- sby files from other projects likely work with minor changes.

## Why Use This Extension?
- Streamlines **PSL** simulation execution.

[![Test](https://github.com/timonix/OneWare.PslExtension/actions/workflows/test.yml/badge.svg)](https://github.com/GithubUser/OneWare.PslExtension/actions/workflows/test.yml)
[![Publish](https://github.com/timonix/OneWare.PslExtension/actions/workflows/publish.yml/badge.svg)](https://github.com/GithubUser/OneWare.PslExtension/actions/workflows/publish.yml)
