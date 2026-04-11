const { chromium } = require('playwright-extra');
const stealth = require('puppeteer-extra-plugin-stealth')();
chromium.use(stealth);

// Parse arguments (e.g., node navigate.js --proxy=ip:port:user:pass)
const args = process.argv.slice(2);
const proxyArg = args.find(a => a.startsWith('--proxy='));
let proxyObj = undefined;

if (proxyArg) {
    const proxyStr = proxyArg.split('=')[1];
    const parts = proxyStr.split(':');
    if (parts.length >= 2) {
        proxyObj = { server: `http://${parts[0]}:${parts[1]}` };
        if (parts.length >= 4) {
            proxyObj.username = parts[2];
            proxyObj.password = parts[3];
        }
    }
}

(async () => {
    // We launch chromium assuming Xvfb is running on DISPLAY=:99
    const browser = await chromium.launch({
        headless: false, // Important to be false so it renders on Xvfb for noVNC
        proxy: proxyObj,
        args: [
            '--no-sandbox',
            '--disable-setuid-sandbox',
            '--disable-dev-shm-usage',
            '--disable-blink-features=AutomationControlled'
        ]
    });

    const context = await browser.newContext({
        userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'
    });
    
    const page = await context.newPage();

    try {
        console.log("Navigating to target booking site...");
        
        // Target booking site URL (replace with actual URL)
        const response = await page.goto('https://api.ipify.org/', { waitUntil: 'load', timeout: 30000 });
        
        // 429 Detection logic
        if (response && (response.status() === 429 || response.status() === 403)) {
            console.error("ERROR:429"); // ContainerMonitor will read this
            await browser.close();
            process.exit(1); 
        }

        const pageContent = await page.content();
        
        if (pageContent.includes('Access Denied') || pageContent.includes('Request blocked')) {
            console.error("ERROR:403"); 
            await browser.close();
            process.exit(1);
        }

        // Simulating booking loop
        while (true) {
            console.log("Checking for slots...");
            
            // Check if Captcha is present
            const isCaptchaPresent = await page.locator('iframe[src*="recaptcha"], iframe[src*="hcaptcha"], div.g-recaptcha').count() > 0;
            if (isCaptchaPresent || pageContent.includes('Please solve captcha')) {
                console.log("SLOT_FOUND"); // Tells the Agent to emit ManualRequired event
                
                // Infinite wait block. Automation pauses here so the human can connect via noVNC
                console.log("Waiting for human intervention through VNC...");
                await new Promise(r => setTimeout(r, 86400000)); // wait 24h
            }

            // Logic to click around and find dates.
            // await page.click('#check-availability');
            // await page.waitForTimeout(3000);

            // Refresh periodically
            await page.waitForTimeout(10000); 
            // await page.reload();
        }

    } catch (err) {
        console.error("Script execution failed:", err);
        // If timeout or connection fails, it might be a bad proxy
        console.error("ERROR:429"); // Trigger proxy rotation on network failure
        await browser.close();
        process.exit(1);
    }
})();
