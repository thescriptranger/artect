# Artect

Artect is a .NET global tool that scaffolds a Clean Architecture + Minimal API solution from an existing SQL Server database.

## Install

    dotnet tool install -g Artect

## Usage

    artect new --connection "Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True"

or replay a saved configuration:

    artect new --config artect.yaml --connection "..."

## Documentation

- Product spec: [`docs/can-you-write-a-soft-sprout.md`](docs/can-you-write-a-soft-sprout.md)
- Design: [`docs/superpowers/specs/2026-04-22-artect-design.md`](docs/superpowers/specs/2026-04-22-artect-design.md)

## License

MIT
