/**
 * Ends the host at start with the reason as its only line on standard error. A thrown error would also print a
 * source excerpt and a stack, which say nothing about the variable to set.
 */
export function refuse(reason: string): never {
  console.error(reason);
  process.exit(1);
}
