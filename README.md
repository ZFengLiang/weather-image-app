# Weather Image App

A small cloud project for my **Server-Side Programming** course.

The idea is simple: you call one API endpoint, and in the background the app pulls
live Dutch weather data, generates a batch of images with that weather stamped on
them, and gives you back links to view them. It runs on Azure and is built to show
the **serverless + queue** pattern — the first request returns instantly, and the
slow work (making ~40 images) happens in the background.

## What it does

1. You send `POST /api/jobs` > you get a job id back straight away.
2. Behind the scenes it fetches ~40 Dutch weather stations, then creates one image
   per station (a photo with that station's weather drawn on top).
3. Every image is saved to private Blob Storage.
4. You send `GET /api/jobs/{id}` → you see the progress, and once it's finished you
   get links to all the generated images.

```
POST /api/jobs > start-queue > fan out 40 stations > image-queue > make + store each image
                                                                                │
GET /api/jobs/{id} ◄───────── progress + image links ◄──────────────────────────┘
```

## What I've done so far

- Built the four functions: **start a job**, **fan out** the work, **make each image**, and **check status**.
- Set up **two queues** so the heavy work runs in the background (`start-queue` > `image-queue`).
- Saved every image to **Blob Storage** and served them with short-lived **SAS links** (the container stays private).
- Tracked job and per-image progress in **Table Storage** for the status endpoint.
- Wrote the whole infrastructure as a **Bicep** template plus a one-command **deploy script**.
- **Deployed it to Azure and tested it end to end** — a full run generated 40/40 images successfully.

## What I used

| Tool | Why |
| --- | --- |
| **C# / .NET 8** (Azure Functions, isolated worker) | the app itself |
| **Azure Functions — Flex Consumption** | serverless host |
| **Azure Queue Storage** | two queues for the background fan-out |
| **Azure Blob Storage** | stores the generated images (served via SAS) |
| **Azure Table Storage** | tracks job + image progress |
| **SixLabors.ImageSharp** | draws the weather text onto the images |
| **Buienradar API** | live Dutch weather data |
| **Picsum Photos** | free base images, no API key needed |
| **Bicep + Azure CLI** | infrastructure and deployment |

## How to run it

### Option 1 — try the live API

It's already deployed. Start a job:

```bash
curl -X POST https://weatherimg-z7idlr2ityrks.azurewebsites.net/api/jobs
```

Copy the `jobId` from the response, then check on it (repeat until `status` is `Completed`):

```bash
curl https://weatherimg-z7idlr2ityrks.azurewebsites.net/api/jobs/PASTE_JOB_ID_HERE
```

When it's done, the `images` field holds a link for each generated image.
You can also use `WeatherImages/api.http` with the VS Code REST Client extension.

### Option 2 — run it locally

You need the [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
and [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
(a local storage emulator). Start Azurite, then:

```bash
cd WeatherImages
func start
```

`local.settings.json` already points storage at the emulator, so there's nothing to
configure. The API runs at `http://localhost:7071`.

### Option 3 — deploy your own copy

```bash
az login
```

```bash
./deploy.ps1 -ResourceGroup ssp-weatherimg -Location germanywestcentral
```

The script publishes the code, creates all the Azure resources from the Bicep
template, deploys it, and prints your API URL at the end.

## Project layout

```
WeatherImages/
  Program.cs       # sets up the storage clients and services
  Functions/       # the 4 functions
  Services/        # weather, image drawing, blob (SAS), status table, queue
  Fonts/           # a bundled font so text renders on any host
  api.http         # ready-made API requests to try
infra/main.bicep   # all the Azure resources
deploy.ps1         # publish + create resources + deploy, in one go
```

## Good to know

- The images container is **private** — the status endpoint hands out temporary SAS
  links instead of making anything public.
- I'm on an **Azure for Students** subscription, which blocks the classic Consumption
  plan and most regions, so the app runs on **Flex Consumption** in `germanywestcentral`.
  The allowed regions are `germanywestcentral`, `switzerlandnorth`, `polandcentral`,
  `italynorth`, and `spaincentral` — pass any of them as `-Location`.
