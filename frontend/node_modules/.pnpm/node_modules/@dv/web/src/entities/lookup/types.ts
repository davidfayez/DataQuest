/** The shape every admin-managed lookup is returned in. */
export interface LookupDto {
  id: string;
  /** Resolved from Accept-Language by the server. */
  name: string;
  nameAr: string;
  nameEn: string;
  isActive: boolean;
}
