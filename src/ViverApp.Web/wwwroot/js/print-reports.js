export async function printReport() {
    await document.fonts.ready;
    await Promise.all(Array.from(document.querySelectorAll('.appointment-print-document img'))
        .map(image => image.decode().catch(() => {})));
    window.print();
}
