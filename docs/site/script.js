const copyButton = document.querySelector('#copy');
const copyStatus = document.querySelector('#copy-status');
copyButton.addEventListener('click', async () => {
  const commands = document.querySelector('#commands').textContent;
  try {
    await navigator.clipboard.writeText(commands);
    copyStatus.textContent = 'Command copied. Paste it into Windows PowerShell and press Enter.';
    copyButton.textContent = 'COPIED ✓';
    setTimeout(() => { copyButton.textContent = 'COPY COMMAND ▣'; }, 2500);
  } catch {
    copyStatus.textContent = 'Select the command above and copy it manually.';
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(document.querySelector('#commands'));
    selection.removeAllRanges();
    selection.addRange(range);
  }
});
