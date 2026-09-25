# URBANOVA
AI-powered Urban Climate & Site Intelligence Platform  
Grow Cooler Cities, One Decision at a Time.


## What is URBANOVA?
URBANOVA helps real estate developers, consulting firms, and engineers analyze a site before and during design, and make design decisions based on data instead of experience or manual estimation alone.

The platform ingests site data — land surface temperature, green area coverage, building density, sun exposure, shading, wind, and more — entirely from open satellite imagery and open climate datasets, then uses AI to identify areas and factors negatively affecting Outdoor Comfort (starting with Urban Heat Island risk).

Because URBANOVA relies on openly available data rather than on-site hardware, any site can be analyzed remotely, at no acquisition cost, anywhere satellite and climate coverage exists.

## What it delivers
- **Site Assessment & Environmental Report** — current site status, key problem areas, contributing factors, and a data-driven Future Outlook
- **Findings & Recommendations** — ranked mitigation options (e.g. increased green cover, shading structures, material changes) compared by expected impact, feasibility, and preliminary cost
- **Scenario Comparison** — current state vs. proposed interventions, to support developer/consultant decision-making

## MVP Scope
The MVP focuses on Urban Heat as the first strong use case: open satellite + climate data → AI-based risk classification → prescriptive, cost-aware recommendations.

## Roadmap
The architecture is designed to extend without a rebuild:

- **Year-round intelligence:** winter use cases (rainfall, humidity, air quality, water accumulation prediction) on the same data pipeline
- **Design-phase support:** pre-design site insights for engineers, reducing manual site analysis time
- **3D Architecture Simulation:** a digital twin of the project enabling virtual walkthroughs — usable both as a real estate marketing tool and, more importantly, connected to the same Analysis Engine to simulate the impact of design changes
- **Smart City expansion:** broader environmental intelligence use cases beyond individual sites

## Tech Direction
- **Backend:** .NET
- **AI/ML:** Python (geospatial & climate data processing, prediction models)
- **Data sources:** open satellite imagery and open climate datasets (e.g. Landsat, Sentinel, ECOSTRESS, ERA5) — specific datasets still being finalized

This README will be expanded with setup instructions, architecture diagrams, and API documentation as the codebase develops.
