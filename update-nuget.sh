# !/usr/bash
# Updates the YoutubeExplode nuget packages to their latest version to fix Youtube API errors
dotnet remove package YoutubeExplode
dotnet remove package YoutubeExplode.Converter
dotnet add package YoutubeExplode
dotnet add package YoutubeExplode.Converter
