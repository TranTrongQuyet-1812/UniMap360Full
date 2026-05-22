const fs = require('fs');
const path = require('path');

const files = [
    {
        src: '../wwwroot/css/responsive.css',
        dest: '../wwwroot/css/responsive.min.css'
    },
    {
        src: '../wwwroot/css/map.css',
        dest: '../wwwroot/css/map.min.css'
    }
];

files.forEach(file => {
    const srcPath = path.join(__dirname, file.src);
    const destPath = path.join(__dirname, file.dest);

    try {
        let css = fs.readFileSync(srcPath, 'utf8');
        
        // Simple CSS Minification
        css = css
            .replace(/\/\*[\s\S]*?\*\//g, '') // Remove comments
            .replace(/\s+/g, ' ')             // Collapse whitespace
            .replace(/\s*([\{\}:;,])\s*/g, '$1') // Remove spaces around delimiters
            .replace(/;}/g, '}')              // Remove trailing semicolons
            .trim();
            
        fs.writeFileSync(destPath, css, 'utf8');
        console.log(`Successfully minified ${path.basename(file.src)} to ${path.basename(file.dest)}!`);
    } catch (err) {
        console.error(`Error minifying ${file.src}:`, err);
        process.exit(1);
    }
});
