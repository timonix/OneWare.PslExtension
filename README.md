![image](https://raw.githubusercontent.com/timonix/OneWare.PslExtension/main/Icon.png)

### Get Started

# PSL Extension

## Overview
This extension integrates **PSL** as a simulator with OneWare Studio, enabling direct interaction with PSL simulations without additional configuration overhead.
Should support Windows, Linux and MacOS. But only tested on Windows.

## Requirements
To use this extension, ensure you have a container runtime installed and properly configured. Currently tested with **Docker Desktop** and **Rancher Desktop**.

## Installation
- Install extension
- Install a container runtime

## Usage
- Launch your container runtime, if not already running
- PSL simulator now shows up in simulator list, simulate will now create a .sby file with some default settings and try to run it.
- Docker image is downloaded automatically on first run, may take a while (about 1 GB)

## Notes
- The generated sby files are a starting point. You may have to edit paths or settings. 
- sby files from other projects likely works with minor changes.

## Why Use This Extension?
- Streamlines **PSL** simulation execution.

[![Test](https://github.com/timonix/OneWare.PslExtension/actions/workflows/test.yml/badge.svg)](https://github.com/GithubUser/OneWare.PslExtension/actions/workflows/test.yml)
[![Publish](https://github.com/timonix/OneWare.PslExtension/actions/workflows/publish.yml/badge.svg)](https://github.com/GithubUser/OneWare.PslExtension/actions/workflows/publish.yml)
