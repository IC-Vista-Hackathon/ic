/** Lookup succeeded but every invoice on the account is already paid. */
export class NoOpenBillError extends Error {
  constructor() {
    super('No open bill was found for that account.');
    this.name = 'NoOpenBillError';
  }
}
