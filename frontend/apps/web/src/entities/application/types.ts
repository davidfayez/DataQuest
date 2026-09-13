/** Mirrors the API's ApplicationStatus enum. Numeric so it matches the wire format exactly. */
export enum ApplicationStatus {
  Draft = 0,
  PendingPayment = 1,
  Pending = 2,
  InProgress = 3,
  MissedInfo = 4,
  Success = 5,
  Failed = 6,
  Refunded = 7,
}

export enum NameLanguageType {
  Arabic = 0,
  English = 1,
}

export enum ApplicationFileKind {
  UserUpload = 0,
  AdminResult = 1,
}

export interface LookupRef {
  id: string;
  name: string;
}

export interface TransactionTypeDto {
  id: string;
  countryIds: string[];
  name: string;
  isActive: boolean;
}

export interface SubTransactionTypeDto {
  id: string;
  transactionTypeId: string;
  name: string;
  isActive: boolean;
}

export interface AuthorityDto {
  id: string;
  countryId: string;
  name: string;
  isActive: boolean;
  subTransactionTypeIds: string[];
}

export interface RequiredFileDto {
  id: string;
  serviceTypeId: string;
  name: string;
  isMandatory: boolean;
}

export interface ServiceTypeCostDto {
  currencyId: string;
  currencyCode: string | null;
  cost: number;
  expressCost: number;
}

export interface ServiceTypeDto {
  id: string;
  verificationAuthorityId: string;
  subTransactionTypeId: string;
  name: string;
  description: string | null;
  executionTimeDays: number;
  cost: number;
  enableExpress: boolean;
  expressCost: number;
  /** Admin-authored express note; null falls back to the wizard's own wording. */
  expressNote: string | null;
  isActive: boolean;
  costs: ServiceTypeCostDto[];
  requiredFiles: RequiredFileDto[];
  /** Locale codes this service's result may be issued in, configured per service by an admin. */
  outputLanguages: string[];
}

export interface ApplicationNameDto {
  languageType: NameLanguageType;
  firstName: string;
  middleName: string | null;
  lastName: string;
}

export interface ApplicationServiceDto {
  id: string;
  serviceTypeId: string;
  serviceName: string;
  description: string | null;
  executionTimeDays: number;
  quantity: number;
  languageCode: string;
  enableExpress: boolean;
  isExpress: boolean;
  unitCost: number;
  expressCost: number;
  lineTotal: number;
}

export interface ApplicationFileDto {
  id: string;
  applicationServiceId: string | null;
  requiredFileId: string | null;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  kind: ApplicationFileKind;
  uploadedAtUtc: string;
  /**
   * What to call the file once it is saved: application, order and the document it satisfies.
   * The blob download names itself, so this is the name that actually reaches the disk.
   */
  downloadName: string;
}

/** Server-computed status of one required document on one service line. */
export interface RequiredFileFieldOptionDto {
  value: string;
  label: string;
}

export interface RequiredFileFieldDto {
  id: string;
  name: string;
  /** Numeric, matching the API's RequiredFieldType enum. */
  fieldType: RequiredFieldType;
  isRequired: boolean;
  sortOrder: number;
  minLength: number | null;
  maxLength: number | null;
  pattern: string | null;
  minValue: number | null;
  maxValue: number | null;
  dateRule: RequiredFieldDateRule;
  minDate: string | null;
  maxDate: string | null;
  options: RequiredFileFieldOptionDto[];
}

/** Mirrors the API's RequiredFieldType enum. */
export enum RequiredFieldType {
  Text = 0,
  Number = 1,
  Date = 2,
  Dropdown = 3,
}

/** Mirrors the API's RequiredFieldDateRule enum. */
export enum RequiredFieldDateRule {
  Any = 0,
  PastOnly = 1,
  FutureOnly = 2,
}

export interface RequiredFileStatusDto {
  requiredFileId: string;
  applicationServiceId: string;
  name: string;
  isMandatory: boolean;
  isSatisfied: boolean;
  /** Effective per-document upload cap in bytes. */
  maxSizeBytes: number;
  maxFiles: number;
  uploadedCount: number;
  fields: RequiredFileFieldDto[];
  /** What the applicant has entered so far, keyed by field id. */
  values: Record<string, string>;
  areFieldsComplete: boolean;
  /** The file extensions this document accepts, already resolved to the default if none. */
  allowedExtensions: string[];
}

export interface ApplicationDetailsDto {
  id: string;
  applicationNumber: string;
  addressedTo: string;
  /** Null on a draft saved before the personal step was completed. */
  birthDate: string | null;
  /** The applicant's own contact details; null until the personal step is filled in. */
  applicantEmail: string | null;
  /** ISO 3166-1 alpha-2 of the dial code chosen, e.g. `EG`. */
  applicantPhoneCountry: string | null;
  /** The dial prefix itself, e.g. `+20`. */
  applicantPhoneCode: string | null;
  /** The national number, digits only. */
  applicantPhoneNumber: string | null;
  status: ApplicationStatus;
  statusName: string;
  isPaid: boolean;
  paidAtUtc: string | null;
  totalCost: number;
  currencyCode: string;
  createdAtUtc: string;
  /** Authoritative capability flags — the UI never re-derives these. */
  canEdit: boolean;
  canDelete: boolean;
  canRefund: boolean;
  /** Null on a draft saved before the services step was reached. */
  transactionType: LookupRef | null;
  subTransactionType: LookupRef | null;
  verificationAuthority: LookupRef | null;
  names: ApplicationNameDto[];
  services: ApplicationServiceDto[];
  files: ApplicationFileDto[];
  requiredFiles: RequiredFileStatusDto[];
  /** Documents the review team attached and chose to share. Internal ones never arrive here. */
  documents: ApplicationDocumentDto[];
}

/** A document a reviewer attached, with the details they recorded beside it. */
export interface ApplicationDocumentDto {
  id: string;
  name: string;
  attachedAtUtc: string;
  files: ApplicationFileDto[];
  fields: DocumentFieldValueDto[];
}

export interface DocumentFieldValueDto {
  id: string;
  name: string;
  fieldType: number;
  value: string | null;
  /** What to show: a dropdown's label, or the raw value for every other type. */
  displayValue: string | null;
}

/** Request shape shared by create and update. */
export interface ApplicationWriteModel {
  addressedTo: string;
  /** Null on a draft saved before the personal step was completed. */
  birthDate: string | null;
  applicantEmail: string | null;
  applicantPhoneCountry: string | null;
  applicantPhoneCode: string | null;
  applicantPhoneNumber: string | null;
  names: Array<{
    languageType: NameLanguageType;
    firstName: string;
    middleName: string | null;
    lastName: string;
  }>;
  transactionTypeId: string | null;
  subTransactionTypeId: string | null;
  verificationAuthorityId: string | null;
  services: Array<{
    serviceTypeId: string;
    quantity: number;
    languageCode: string;
    isExpress: boolean;
  }>;
}
