const copyButton = document.querySelector('#copy');
const copyStatus = document.querySelector('#copy-status');
copyButton.addEventListener('click', async () => {
  const commands = document.querySelector('#commands').textContent;
  try {
    await navigator.clipboard.writeText(commands);
    copyStatus.textContent = 'Commands copied. Run them in Windows PowerShell.';
    copyButton.textContent = 'COPIED ✓';
    setTimeout(() => { copyButton.textContent = 'COPY COMMANDS ▣'; }, 2500);
  } catch {
    copyStatus.textContent = 'Select the commands above and copy them manually.';
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(document.querySelector('#commands'));
    selection.removeAllRanges();
    selection.addRange(range);
  }
});
