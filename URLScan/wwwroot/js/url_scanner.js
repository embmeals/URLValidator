const STATUS = {
    NO_INDEX: 'NoIndex Found',
    POTENTIAL_NO_INDEX: 'Potential NoIndex',
    INDEXED: 'Indexed',
    NOT_FOUND: '404 Not Found',
    SERVER_ERROR: 'Server Error',
    INVALID_URL: 'Invalid URL'
};

const STATUS_ORDER = {
    [STATUS.NO_INDEX]: 1,
    [STATUS.POTENTIAL_NO_INDEX]: 2,
    [STATUS.INVALID_URL]: 3,
    [STATUS.NOT_FOUND]: 4,
    [STATUS.SERVER_ERROR]: 5,
    [STATUS.INDEXED]: 6
};

const BATCH_SIZE = 50;
const BATCH_DELAY_MS = 500;

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

Vue.createApp({
    data() {
        return {
            file: null,
            urls: [],
            results: [],
            error: "",
            processing: false,
            complete: false,
            currentPage: 1,
            pageSize: 50,
            summary: {
                indexed: 0,
                noIndex: 0,
                notFound: 0,
                errors: 0
            }
        };
    },
    computed: {
        totalPages() {
            return Math.ceil(this.results.length / this.pageSize);
        },
        paginatedResults() {
            const start = (this.currentPage - 1) * this.pageSize;
            return this.results.slice(start, start + this.pageSize);
        },
        progressPercent() {
            if (!this.urls.length) return 0;
            return Math.round((this.results.length / this.urls.length) * 100);
        },
        statusMessage() {
            if (this.complete) {
                return `${this.urls.length} URLs processed`;
            }
            return `Processing: ${this.results.length}/${this.urls.length} URLs (${this.progressPercent}%)`;
        },
        visiblePages() {
            const startPage = Math.max(1, this.currentPage - Math.floor(5 / 2));
            const endPage = Math.min(this.totalPages, startPage + 4);
            return Array.from({ length: endPage - startPage + 1 }, (_, i) => startPage + i);
        }
    },
    methods: {
        onFileChange(event) {
            const uploadedFile = event.target.files[0];
            if (!uploadedFile) {
                this.error = "No file selected.";
                return;
            }
            this.readFile(uploadedFile);
        },
        readFile(file) {
            const reader = new FileReader();
            reader.onload = e => {
                this.handleFileContent(file.name, e.target.result);
            };
            reader.readAsText(file);
        },
        handleFileContent(filename, content) {
            this.urls = this.extractUrls(filename, content);
            if (!this.urls.length) {
                this.error = "File is empty or contains no valid URLs.";
                return;
            }
            this.results = [];
            this.validateUrls();
        },
        extractUrls(filename, content) {
            const isCsv = filename.toLowerCase().endsWith(".csv");
            return [...new Set(content
                .split(/\r?\n/)
                .map(line => line.trim())
                .filter(line => line)
                .map(line => isCsv ? this.extractUrlFromCsv(line) : this.extractUrlFromText(line))
                .filter(url => url)
            )];
        },
        extractUrlFromCsv(line) {
            const url = line.split(",")[0]?.trim();
            return this.isValidUrl(url) ? url : null;
        },
        extractUrlFromText(line) {
            return this.isValidUrl(line) ? line : null;
        },
        isValidUrl(url) {
            return url && url.startsWith("http");
        },
        async validateUrls() {
            if (!this.urls.length) {
                this.error = "No URLs provided.";
                return;
            }
            this.resetState();
            this.processing = true;

            // Process in batches to avoid overwhelming the server
            const batches = this.chunkArray(this.urls, BATCH_SIZE);
            for (const batch of batches) {
                await this.processBatch(batch);
                await sleep(BATCH_DELAY_MS);
            }

            this.addMissingResults();
            this.sortResults();
            this.updateSummary();

            this.processing = false;
            this.complete = true;
        },
        resetState() {
            this.error = "";
            this.results = [];
            this.currentPage = 1;
            this.summary = { indexed: 0, noIndex: 0, notFound: 0, errors: 0 };
        },
        chunkArray(array, size) {
            const chunks = [];
            for (let i = 0; i < array.length; i += size) {
                chunks.push(array.slice(i, i + size));
            }
            return chunks;
        },
        async processBatch(batch) {
            try {
                const response = await axios.post('/api/UrlValidation/validate', batch);
                if (response.data && Array.isArray(response.data)) {
                    // Convert any server-timeout errors to "404 Not Found"
                    const processed = response.data.map(result => {
                        if (
                            result.status === STATUS.SERVER_ERROR &&
                            result.details?.includes('timed out')
                        ) {
                            return {
                                ...result,
                                status: STATUS.NOT_FOUND,
                                details: 'Page does not exist (request timed out)'
                            };
                        }
                        return result;
                    });
                    this.results.push(...processed);
                }
            } catch (err) {
                this.error = `Error: ${err.response ? err.response.data : "Server unreachable"}`;
            }
        },
        addMissingResults() {
            const processedUrls = new Set(this.results.map(r => r.url.toLowerCase()));
            this.urls.forEach(url => {
                if (!processedUrls.has(url.toLowerCase())) {
                    this.results.push({
                        url,
                        status: STATUS.INVALID_URL,
                        details: 'URL was not processed by the server',
                        category: ''
                    });
                }
            });
        },
        updateSummary() {
            this.summary.indexed = this.results.filter(r => r.status === STATUS.INDEXED).length;
            this.summary.noIndex = this.results.filter(r =>
                r.status === STATUS.NO_INDEX || r.status === STATUS.POTENTIAL_NO_INDEX
            ).length;
            this.summary.notFound = this.results.filter(r => r.status === STATUS.NOT_FOUND).length;
            this.summary.errors = this.results.filter(r =>
                ![STATUS.INDEXED, STATUS.NO_INDEX, STATUS.POTENTIAL_NO_INDEX, STATUS.NOT_FOUND].includes(r.status)
            ).length;
        },
        sortResults() {
            this.results.sort((a, b) => (STATUS_ORDER[a.status] || 999) - (STATUS_ORDER[b.status] || 999));
        },
        changePage(page) {
            if (page >= 1 && page <= this.totalPages) {
                this.currentPage = page;
            }
        },
        getRowClass(result) {
            if ([STATUS.NO_INDEX, STATUS.POTENTIAL_NO_INDEX].includes(result.status)) {
                return 'table-danger';
            }
            if (result.status === STATUS.INVALID_URL) {
                return 'table-warning';
            }
            if (result.status === STATUS.NOT_FOUND) {
                return 'table-info';
            }
            if (result.status === STATUS.SERVER_ERROR) {
                return 'table-secondary';
            }
            if (result.status === STATUS.INDEXED) {
                return 'table-success';
            }
            return '';
        }
    }
}).mount('#app');