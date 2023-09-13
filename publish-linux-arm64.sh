#!/bin/bash
dotnet publish --configuration Release --runtime linux-arm64 --self-contained /p:PublishSingleFile=true