# URL Scanner

A simple ASP.NET Core and Vue.js application that **validates URLs**, checks for **noindex** tags, extracts **meta tags**, and displays results in a user-friendly interface.

## Overview

This project allows you to upload a file containing URLs (in `.txt` or `.csv` format). It then:

* **Validates** whether each URL is well-formed.
* **Makes an HTTP request** to see if the page is accessible (200, 404, 500, etc.).
* **Checks for**:
   * **NoIndex** directives in both HTTP headers (`X-Robots-Tag`) and meta tags (`<meta name="robots" ...>`).
   * **Other meta tags** (optionally displayed in the UI).
* **Displays** the results in a Vue-powered web interface, including status, category, meta tags, and more.

## Prerequisites

* **.NET 7 SDK** (or whichever version your project targets)
* **Node.js** (if you plan on bundling front-end assets, though this project uses a simple CDN approach)
* A modern browser (Chrome, Firefox, Edge) to run the front-end UI.

## Getting Started

1. **Clone the Repository**

```bash
git clone https://github.com/YourUsername/url-scanner.git
cd url-scanner
```

2. **Restore and Build**

```bash
dotnet restore
dotnet build
```

3. **Run the Application**

```bash
dotnet run
```

By default, it will listen on `http://localhost:5114` (or another port if configured).

4. **Open Your Browser**
Navigate to `http://localhost:5114` and you'll see the "URL Validator" interface.

## Usage

1. **Select a File** containing URLs (one per line or CSV format).
2. **Click "Validate URLs"** to begin processing.
3. The app **batches** URLs (configurable by `BATCH_SIZE` in `url_scanner.js`) and sends them to `/api/UrlValidation/validate`.
4. Watch the progress bar update.
5. **Results**:
   * **Status** (Indexed, 404, Invalid, NoIndex Found, etc.)
   * **Category** (Jobs, Articles, News, etc.)
   * **Details** (HTTP errors, timeouts, etc.)
   * **Meta Tags** (if enabled, shows `robots`, `og:url`, or all tags depending on your filtering logic).


![image](https://github.com/user-attachments/assets/0c0e7969-cf5c-411d-a67b-c5a245778cab)
![image](https://github.com/user-attachments/assets/723c65db-2332-4902-9a32-c9612ff5023f)
![image](https://github.com/user-attachments/assets/3d3e1fa5-bcc8-4a7a-9f18-addb46b2015c)


