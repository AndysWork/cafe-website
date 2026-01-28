export interface Staff {
  id?: string;
  _id?: string;
  employeeId: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  alternatePhoneNumber?: string;
  dateOfBirth?: Date | string;
  gender?: string;
  address?: StaffAddress;
  emergencyContact?: EmergencyContact;

  // Employment Details
  position: string;
  department?: string;
  employmentType: string;
  hireDate: Date | string;
  probationEndDate?: Date | string;
  terminationDate?: Date | string;
  isActive: boolean;

  // Compensation
  salary: number;
  salaryType: string;
  bankDetails?: BankDetails;

  // Outlet Assignment - Staff can work at multiple outlets
  outletIds: string[];

  // Work Schedule
  workingDays: string[];
  shiftStartTime?: string;
  shiftEndTime?: string;

  // Documents
  documents: StaffDocument[];

  // Performance & Notes
  performanceRating?: number;
  notes?: string;
  skills: string[];

  // Audit Fields
  createdAt?: Date | string;
  createdBy?: string;
  updatedAt?: Date | string;
  updatedBy?: string;

  // Leave Balance
  annualLeaveBalance: number;
  sickLeaveBalance: number;
  casualLeaveBalance: number;
}

export interface StaffAddress {
  street?: string;
  city?: string;
  state?: string;
  postalCode?: string;
  country?: string;
}

export interface EmergencyContact {
  name?: string;
  relationship?: string;
  phoneNumber?: string;
  alternatePhoneNumber?: string;
}

export interface BankDetails {
  accountHolderName?: string;
  accountNumber?: string;
  bankName?: string;
  ifscCode?: string;
  branchName?: string;
}

export interface StaffDocument {
  documentType: string;
  documentNumber?: string;
  documentUrl?: string;
  uploadedAt?: Date | string;
  expiryDate?: Date | string;
  isVerified: boolean;
}

export interface StaffStatistics {
  totalStaff: number;
  activeStaff: number;
  inactiveStaff: number;
  fullTimeStaff: number;
  partTimeStaff: number;
  contractStaff: number;
  staffByPosition: { [key: string]: number };
  staffByDepartment: { [key: string]: number };
}

export interface UpdateSalaryRequest {
  salary: number;
}

export interface UpdatePerformanceRatingRequest {
  rating: number;
}

export interface UpdateLeaveBalancesRequest {
  annualLeave: number;
  sickLeave: number;
  casualLeave: number;
}

// Constants for dropdowns
export const EMPLOYMENT_TYPES = ['Full-Time', 'Part-Time', 'Contract'];
export const SALARY_TYPES = ['Monthly', 'Daily', 'Hourly'];
export const GENDERS = ['Male', 'Female', 'Other'];
export const DAYS_OF_WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

export const COMMON_POSITIONS = [
  'Manager',
  'Assistant Manager',
  'Barista',
  'Senior Barista',
  'Chef',
  'Sous Chef',
  'Cook',
  'Waiter',
  'Cashier',
  'Supervisor',
  'Kitchen Helper',
  'Cleaner',
  'Delivery Person'
];

export const DEPARTMENTS = [
  'Management',
  'Service',
  'Kitchen',
  'Operations',
  'Administration',
  'Delivery'
];

export const DOCUMENT_TYPES = [
  'Aadhar Card',
  'PAN Card',
  'Passport',
  'Driving License',
  'Voter ID',
  'Resume',
  'Offer Letter',
  'Experience Certificate',
  'Education Certificate',
  'Police Verification',
  'Medical Certificate',
  'Bank Passbook',
  'Photo ID'
];
