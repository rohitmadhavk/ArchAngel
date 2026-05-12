// Error handler to catch any JavaScript errors
window.onerror = function(message, source, lineno, colno, error) {
    console.error('[WEBVIEW] JavaScript Error:', { message, source, lineno, colno, error });
    const debugInfo = document.getElementById('debugInfo');
    if (debugInfo) {
        debugInfo.textContent = 'JS ERROR: ' + message;
        debugInfo.style.backgroundColor = 'red';
    }
    return false;
};

// Immediate logging
console.log('[WEBVIEW] ===== WEBVIEW SCRIPT STARTING =====');

// Declare vscode in global scope
let vscode;
try {
    console.log('[WEBVIEW] Attempting to acquire vscode API...');
    vscode = acquireVsCodeApi();
    console.log('[WEBVIEW] vscode API acquired successfully');
} catch (error) {
    console.error('[WEBVIEW] Failed to acquire vscode API:', error);
}

const chatContainer = document.getElementById('chatContainer');
const messageInput = document.getElementById('messageInput');
const sendButton = document.getElementById('sendButton');
const typingIndicator = document.getElementById('typingIndicator');
const debugInfo = document.getElementById('debugInfo');

let messageCount = 0;

// Enhanced markdown parser
function parseMarkdown(text) {
    if (!text) return '';
    
    // Escape HTML first
    let html = text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
    
    // Tables - handle before other processing
    html = html.replace(/^\|(.+)\|\s*$/gm, (match, content) => {
        const cells = content.split('|').map(cell => cell.trim());
        return `<tr>${cells.map(cell => `<td>${cell}</td>`).join('')}</tr>`;
    });
    
    // Table headers (look for rows followed by separator rows)
    html = html.replace(/(<tr><td>[^<]*<\/td><\/tr>)\s*\n\s*<tr><td>[-:\s|]+<\/td><\/tr>/g, (match, headerRow) => {
        const headerCells = headerRow.match(/<td>([^<]*)<\/td>/g);
        if (headerCells) {
            const headers = headerCells.map(cell => {
                const content = cell.replace(/<\/?td>/g, '');
                return `<th>${content}</th>`;
            });
            return `<thead><tr>${headers.join('')}</tr></thead>`;
        }
        return match;
    });
    
    // Wrap consecutive table rows in table tags
    html = html.replace(/(<tr>.*?<\/tr>(\s*\n\s*<tr>.*?<\/tr>)*)/gs, '<table>$1</table>');
    
    // Fix table structure - move thead outside of tbody if present
    html = html.replace(/<table>(\s*<thead>.*?<\/thead>)?(\s*<tr>.*?<\/tr>)*\s*<\/table>/gs, (match) => {
        const theadMatch = match.match(/<thead>.*?<\/thead>/s);
        const tbodyRows = match.match(/<tr>(?!.*<th>).*?<\/tr>/gs);
        
        let result = '<table>';
        if (theadMatch) {
            result += theadMatch[0];
        }
        if (tbodyRows && tbodyRows.length > 0) {
            result += '<tbody>' + tbodyRows.join('') + '</tbody>';
        }
        result += '</table>';
        
        return result;
    });
    
    // Headers (# ## ### etc.) - Made larger
    html = html.replace(/^#### (.*$)/gm, '<h4>$1</h4>');
    html = html.replace(/^### (.*$)/gm, '<h3>$1</h3>');
    html = html.replace(/^## (.*$)/gm, '<h2>$1</h2>');
    html = html.replace(/^# (.*$)/gm, '<h1>$1</h1>');
    
    // Code blocks (``` code ```) - handle before other processing.
    // The language tag is restricted to a conservative charset so it can't
    // break out of the data-language attribute (e.g. via `" onmouseover=...`).
    html = html.replace(/```(\w+)?\n([\s\S]*?)```/g, (match, lang, code) => {
        const safeLang = lang && /^[A-Za-z0-9_+\-]{1,32}$/.test(lang) ? lang : '';
        const language = safeLang ? ` data-language="${safeLang}"` : '';
        return `<pre${language}><code>${code.trim()}</code></pre>`;
    });
    
    // Inline code (`code`)
    html = html.replace(/`([^`\n]+)`/g, '<code>$1</code>');
    
    // Bold (**text** or __text__)
    html = html.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
    html = html.replace(/__(.*?)__/g, '<strong>$1</strong>');
    
    // Italic (*text* or _text_) - but not if it's part of a list marker
    html = html.replace(/(?<!^[\s]*[-*+]\s.*)\*([^*\n]+)\*(?!.*)/g, '<em>$1</em>');
    html = html.replace(/(?<!^[\s]*[-*+]\s.*)_([^_\n]+)_(?!.*)/g, '<em>$1</em>');
    
    // Links [text](url) — only allow http(s) and relative URLs. Anything else
    // (javascript:, data:, vbscript:, file:, etc.) is rendered as plain text.
    html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (match, label, url) => {
        const trimmed = url.trim();
        const isSafe = /^https?:\/\//i.test(trimmed) || /^(\/|\.\/|\.\.\/|#|\?)/.test(trimmed);
        if (!isSafe) {
            return `${label} (${trimmed})`;
        }
        // Escape quotes and angle brackets so the URL can't break out of the attribute.
        const safeHref = trimmed
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
        return `<a href="${safeHref}" target="_blank" rel="noopener noreferrer">${label}</a>`;
    });
    
    // Blockquotes (> text)
    html = html.replace(/^> (.*)$/gm, '<blockquote>$1</blockquote>');
    
    // Horizontal rules (--- or ***)
    html = html.replace(/^(---|\*\*\*)$/gm, '<hr>');
    
    // Lists - Process unordered lists first
    const listItems = [];
    html = html.replace(/^[\s]*[-*+]\s+(.*)$/gm, (match, content) => {
        const placeholder = `__LIST_ITEM_${listItems.length}__`;
        listItems.push(content);
        return placeholder;
    });
    
    // Replace list item placeholders with actual list items and wrap in <ul>
    if (listItems.length > 0) {
        listItems.forEach((item, index) => {
            html = html.replace(`__LIST_ITEM_${index}__`, `<li>${item}</li>`);
        });
        
        // Wrap consecutive <li> items in <ul>
        html = html.replace(/(<li>.*<\/li>(\s*<li>.*<\/li>)*)/gs, '<ul>$1</ul>');
    }
    
    // Ordered lists (1. item, 2. item, etc.)
    const orderedItems = [];
    html = html.replace(/^\s*\d+\.\s+(.*)$/gm, (match, content) => {
        const placeholder = `__ORDERED_ITEM_${orderedItems.length}__`;
        orderedItems.push(content);
        return placeholder;
    });
    
    // Replace ordered list item placeholders
    if (orderedItems.length > 0) {
        orderedItems.forEach((item, index) => {
            html = html.replace(`__ORDERED_ITEM_${index}__`, `<li>${item}</li>`);
        });
        
        // Wrap consecutive <li> items in <ol>
        html = html.replace(/(<li>.*<\/li>(\s*<li>.*<\/li>)*)/gs, '<ol>$1</ol>');
    }
    
    // Line breaks
    html = html.replace(/\n\n/g, '</p><p>');
    html = html.replace(/\n/g, '<br>');
    
    // Wrap in paragraphs if not already wrapped
    if (!html.includes('<p>') && !html.includes('<h') && !html.includes('<ul>') && !html.includes('<ol>') && !html.includes('<pre>') && !html.includes('<table>')) {
        html = `<p>${html}</p>`;
    }
    
    return html;
}

function addMessageWithSources(content, isUser = false, sources = null, repositoryCount = 0) {
    messageCount++;
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${isUser ? 'user-message' : 'assistant-message'}`;
    messageDiv.id = `message-${messageCount}`;
    
    // Header
    const headerDiv = document.createElement('div');
    headerDiv.className = 'message-header';
    headerDiv.textContent = isUser ? 'You' : '🤖 Assistant';
    messageDiv.appendChild(headerDiv);
    
    // Main message content with markdown parsing
    const contentDiv = document.createElement('div');
    contentDiv.className = 'message-content';
    
    if (isUser) {
        contentDiv.textContent = content;
    } else {
        contentDiv.innerHTML = parseMarkdown(content);
    }
    
    messageDiv.appendChild(contentDiv);
    
    // Add sources if available
    if (sources && sources.length > 0) {
        const sourcesDiv = document.createElement('div');
        sourcesDiv.className = 'message-sources';
        
        const sourcesHeader = document.createElement('div');
        sourcesHeader.className = 'sources-header';
        sourcesHeader.textContent = `📚 Sources (from ${repositoryCount} repositories):`;
        sourcesDiv.appendChild(sourcesHeader);
        
        const sourcesList = document.createElement('div');
        sourcesList.className = 'sources-list';
        
        sources.forEach((source, index) => {
            const sourceItem = document.createElement('div');
            sourceItem.className = 'source-item';

            const numberSpan = document.createElement('span');
            numberSpan.className = 'source-number';
            numberSpan.textContent = `${index + 1}.`;
            sourceItem.appendChild(numberSpan);

            sourceItem.appendChild(document.createTextNode(' '));

            const link = document.createElement('a');
            link.className = 'source-link';
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            // Only allow http(s) URLs in the href to block javascript:/data: schemes.
            const rawUrl = typeof source.githubUrl === 'string' ? source.githubUrl : '';
            link.href = /^https?:\/\//i.test(rawUrl) ? rawUrl : '#';
            link.textContent = String(source.filePath ?? '');
            sourceItem.appendChild(link);

            sourceItem.appendChild(document.createTextNode(' '));

            const details = document.createElement('span');
            details.className = 'source-details';
            const lang = String(source.language ?? '');
            const startLine = String(source.startLine ?? '');
            const endLine = String(source.endLine ?? '');
            details.textContent = `(${lang}, lines ${startLine}-${endLine})`;
            sourceItem.appendChild(details);

            sourceItem.appendChild(document.createTextNode(' '));

            const repoSpan = document.createElement('span');
            repoSpan.className = 'source-repo';
            repoSpan.textContent = `[${String(source.repository ?? '')}]`;
            sourceItem.appendChild(repoSpan);

            sourcesList.appendChild(sourceItem);
        });
        
        sourcesDiv.appendChild(sourcesList);
        messageDiv.appendChild(sourcesDiv);
    } else if (repositoryCount > 0) {
        // Show that RAG was attempted but no sources found
        const noSourcesDiv = document.createElement('div');
        noSourcesDiv.className = 'no-sources';
        const em = document.createElement('em');
        em.textContent = `Searched ${Number(repositoryCount) || 0} repositories - no specific code references found`;
        noSourcesDiv.appendChild(em);
        messageDiv.appendChild(noSourcesDiv);
    }
    
    chatContainer.appendChild(messageDiv);
    chatContainer.scrollTop = chatContainer.scrollHeight;
}

function addMessage(content, isUser = false, isError = false) {
    messageCount++;
    console.log('[WEBVIEW] addMessage #' + messageCount + ' called');
    
    if (!content) {
        console.error('[WEBVIEW] addMessage called with empty content');
        return;
    }

    if (!chatContainer) {
        console.error('[WEBVIEW] Cannot add message - chatContainer is null');
        return;
    }
    
    try {
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${isUser ? 'user-message' : 'assistant-message'} ${isError ? 'error-message' : ''}`;
        
        const headerDiv = document.createElement('div');
        headerDiv.className = 'message-header';
        headerDiv.textContent = isUser ? 'You' : '🤖 Assistant';
        
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        
        // Apply markdown parsing for assistant messages (non-error)
        if (!isUser && !isError) {
            contentDiv.innerHTML = parseMarkdown(content);
        } else {
            contentDiv.textContent = content;
        }
        
        messageDiv.appendChild(headerDiv);
        messageDiv.appendChild(contentDiv);
        
        console.log('[WEBVIEW] Adding message element to chatContainer');
        chatContainer.appendChild(messageDiv);
        chatContainer.scrollTop = chatContainer.scrollHeight;
        
        // Update debug info
        if (debugInfo) {
            debugInfo.textContent = 'Messages: ' + messageCount + ' | Last: ' + new Date().toLocaleTimeString();
        }
        console.log('[WEBVIEW] Message added successfully');
    } catch (error) {
        console.error('[WEBVIEW] Error in addMessage:', error);
        if (debugInfo) {
            debugInfo.textContent = 'addMessage ERROR: ' + error.message;
            debugInfo.style.backgroundColor = 'red';
        }
    }
}

function sendMessage() {
    console.log('[WEBVIEW] sendMessage called');
    
    if (!messageInput || !vscode) {
        console.error('[WEBVIEW] messageInput or vscode API not available');
        return;
    }

    const message = messageInput.value.trim();
    if (!message) {
        console.log('[WEBVIEW] Empty message, not sending');
        return;
    }

    console.log('[WEBVIEW] Sending message:', message);
    addMessage(message, true);
    
    try {
        vscode.postMessage({
            type: 'chat',
            message: message
        });
        console.log('[WEBVIEW] Message posted successfully');
    } catch (error) {
        console.error('[WEBVIEW] Error posting message:', error);
        addMessage('Error: Could not send message - ' + error.message, false, true);
    }

    // Clear input and disable send button
    messageInput.value = '';
    if (sendButton) {
        sendButton.disabled = true;
    }
}

// Event listeners
if (sendButton) {
    sendButton.addEventListener('click', sendMessage);
    console.log('[WEBVIEW] Send button click listener added');
}

if (messageInput) {
    messageInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    messageInput.addEventListener('input', () => {
        if (sendButton) {
            sendButton.disabled = !messageInput.value.trim();
        }
    });
    console.log('[WEBVIEW] Message input listeners added');
}

// Handle messages from extension
window.addEventListener('message', (event) => {
    console.log('[WEBVIEW] Received message from extension:', event.data);
    
    try {
        const message = event.data;
        
        if (!message || !message.type) {
            console.error('[WEBVIEW] Invalid message structure:', message);
            return;
        }
        
        switch (message.type) {
            case 'test':
                console.log('[WEBVIEW] Received test message:', message.message);
                addMessage('🧪 TEST: ' + message.message, false, false);
                break;
            case 'response':
                console.log('[WEBVIEW] Processing response message');
                if (message.message) {
                    if (message.sources && message.sources.length > 0) {
                        addMessageWithSources(
                            message.message, 
                            false, 
                            message.sources, 
                            message.repositoryCount || 0
                        );
                    } else { 
                        addMessage(message.message);
                    }
                    console.log('[WEBVIEW] Response message added to chat');
                }
                if (sendButton) {
                    sendButton.disabled = false;
                }
                break;
            case 'error':
                console.log('[WEBVIEW] Processing error message:', message.message);
                addMessage(message.message || 'Unknown error occurred', false, true);
                if (sendButton) {
                    sendButton.disabled = false;
                }
                break;
            case 'typing':
                console.log('[WEBVIEW] Processing typing indicator:', message.isTyping);
                if (typingIndicator) {
                    typingIndicator.style.display = message.isTyping ? 'block' : 'none';
                }
                if (!message.isTyping && sendButton) {
                    sendButton.disabled = false;
                }
                break;
            default:
                console.warn('[WEBVIEW] Unknown message type:', message.type);
        }
    } catch (error) {
        console.error('[WEBVIEW] Error processing message:', error);
        if (debugInfo) {
            debugInfo.textContent = 'Message Error: ' + error.message;
            debugInfo.style.backgroundColor = 'red';
        }
    }
});

// Initialize
if (sendButton) {
    sendButton.disabled = true;
}

// Send ready message to extension after initialization
setTimeout(() => {
    console.log('[WEBVIEW] Initialization complete');
    
    if (debugInfo) {
        debugInfo.textContent = 'Ready | vscode: ' + (vscode ? 'Available' : 'Missing');
        debugInfo.style.backgroundColor = vscode ? 'green' : 'orange';
    }
    
    if (vscode) {
        try {
            vscode.postMessage({
                type: 'ready',
                message: 'Webview is ready'
            });
            console.log('[WEBVIEW] Ready message sent to extension');
        } catch (error) {
            console.error('[WEBVIEW] Error sending ready message:', error);
        }
    }
    
    // Test message
    addMessage('🔧 **Markdown rendering is now active!** \n\n- ✅ *Bold* and *italic* text\n- ✅ `inline code`\n- ✅ Code blocks\n- ✅ Lists and more', false, false);
    
}, 1000);

console.log('[WEBVIEW] Script initialization complete');