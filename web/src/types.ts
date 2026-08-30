// Mirrors the API DTOs. Enums are serialized as strings by the backend.

export type SkillLevel = "Beginner" | "Intermediate" | "Advanced" | "Expert";
export type LanguageLevel = "Basic" | "Conversational" | "Professional" | "Fluent" | "Native";
export type QualificationType = "Degree" | "Certification";

export type EmployeeStatus = "Draft" | "Active";

export interface EmployeeSummary {
  id: string;
  firstName: string;
  lastName: string;
  title: string;
  location: string | null;
  email: string;
  currentCapacityPercent: number;
  status: EmployeeStatus;
}

export interface SpokenLanguage {
  id: string;
  language: string;
  level: LanguageLevel;
}

export interface AvailabilityEntry {
  id: string;
  effectiveFrom: string; // ISO date
  capacityPercent: number;
}

export interface EmployeeSkill {
  id: string;
  skillId: string;
  skillName: string;
  categoryName: string;
  level: SkillLevel;
  yearsExperience: number;
}

export interface Qualification {
  id: string;
  type: QualificationType;
  name: string;
  institution: string | null;
  field: string | null;
  startDate: string | null;
  endDate: string | null;
  issuer: string | null;
  credentialId: string | null;
  issueDate: string | null;
  expiryDate: string | null;
}

export interface Achievement {
  id: string;
  order: number;
  text: string;
}

export interface ExperienceSkillRef {
  id: string;
  skillId: string;
  skillName: string;
}

export interface Experience {
  id: string;
  company: string;
  title: string;
  location: string | null;
  startDate: string;
  endDate: string | null;
  summary: string | null;
  achievements: Achievement[];
  skills: ExperienceSkillRef[];
}

export interface EmployeeDetail {
  id: string;
  firstName: string;
  lastName: string;
  title: string;
  email: string;
  phone: string | null;
  location: string | null;
  summary: string | null;
  photoUrl: string | null;
  currentCapacityPercent: number;
  status: EmployeeStatus;
  spokenLanguages: SpokenLanguage[];
  availabilityEntries: AvailabilityEntry[];
  skills: EmployeeSkill[];
  qualifications: Qualification[];
  experiences: Experience[];
}

export interface SaveSpokenLanguage {
  language: string;
  level: LanguageLevel;
}

export interface SaveQualification {
  type: QualificationType;
  name: string;
  institution: string | null;
  field: string | null;
  startDate: string | null;
  endDate: string | null;
  issuer: string | null;
  credentialId: string | null;
  issueDate: string | null;
  expiryDate: string | null;
}

/** Order is the bullet's position on the CV; the server sorts by it. */
export interface SaveAchievement {
  order: number;
  text: string;
}

/** Achievements and skillIds are a full replace on update — the server drops what is absent. */
export interface SaveExperience {
  company: string;
  title: string;
  location: string | null;
  startDate: string;
  endDate: string | null;
  summary: string | null;
  achievements: SaveAchievement[];
  skillIds: string[];
}

export interface SaveEmployee {
  firstName: string;
  lastName: string;
  title: string;
  email: string;
  phone: string | null;
  location: string | null;
  summary: string | null;
  photoUrl: string | null;
}

export interface Category {
  id: string;
  name: string;
  parentId: string | null;
}

export interface CategoryNode {
  id: string;
  name: string;
  children: CategoryNode[];
  skills: SkillDto[];
}

export interface SkillDto {
  id: string;
  name: string;
  categoryId: string;
  categoryName: string;
  rank: number;
}

// CV
export interface Cv {
  fullName: string;
  title: string;
  email: string;
  phone: string | null;
  location: string | null;
  summary: string | null;
  photoUrl: string | null;
  availability: { currentCapacityPercent: number; schedule: AvailabilityEntry[] };
  skillGroups: { category: string; skills: EmployeeSkill[] }[];
  languages: SpokenLanguage[];
  experiences: {
    id: string;
    company: string;
    title: string;
    location: string | null;
    period: string;
    summary: string | null;
    achievements: { id: string; text: string }[];
    skills: string[];
  }[];
  education: Qualification[];
  certifications: Qualification[];
}
