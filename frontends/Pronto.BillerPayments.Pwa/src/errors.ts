/** HTTP failure with its status code preserved so telemetry can bucket it without exporting the message. */
export class RequestError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
    this.name = 'RequestError';
  }
}

/** Lookup succeeded but every invoice on the account is already paid. */
export class NoOpenBillError extends Error {
  constructor() {
    super('No open bill was found for that account.');
    this.name = 'NoOpenBillError';
  }
}
