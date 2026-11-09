import { PlantInfo } from './auth.model';

export interface PermissionInfo {
  code: string;
  group: string;
  name: string;
  description: string;
}

export interface Role {
  roleId: number;
  name: string;
  description: string | null;
  /** Administrator: semua hak akses, tidak bisa diubah/dihapus. */
  isSystem: boolean;
  userCount: number;
  permissions: string[];
}

export interface RoleRequest {
  name: string;
  description: string | null;
  permissions: string[];
}

export interface UserAccount {
  userId: number;
  username: string;
  displayName: string;
  email: string | null;
  deptId: number | null;
  isActive: boolean;
  mustChangePassword: boolean;
  isLocked: boolean;
  lastLoginAt: string | null;
  roleIds: number[];
  plantIds: number[];
}

export interface RoleOption {
  roleId: number;
  name: string;
  isSystem: boolean;
}

export interface UserOptions {
  roles: RoleOption[];
  plants: PlantInfo[];
  departments: { deptId: number; code: string; name: string }[];
}

export interface UpdateUserRequest {
  displayName: string;
  email: string | null;
  deptId: number | null;
  isActive: boolean;
  roleIds: number[];
  plantIds: number[];
}

export interface CreateUserRequest extends UpdateUserRequest {
  username: string;
  password: string;
}
