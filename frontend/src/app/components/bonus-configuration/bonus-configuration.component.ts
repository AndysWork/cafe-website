import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  BonusConfigurationService,
  BonusConfiguration,
  BonusRule,
  CreateBonusConfigurationRequest,
  UpdateBonusConfigurationRequest,
  BonusRuleRequest,
  BonusRuleType,
  CalculationType,
  CalculationPeriod
} from '../../services/bonus-configuration.service';
import { OutletService } from '../../services/outlet.service';
import { StaffService } from '../../services/staff.service';

@Component({
  selector: 'app-bonus-configuration',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './bonus-configuration.component.html',
  styleUrls: ['./bonus-configuration.component.scss']
})
export class BonusConfigurationComponent implements OnInit {
  private bonusConfigService = inject(BonusConfigurationService);
  private outletService = inject(OutletService);
  private staffService = inject(StaffService);

  configurations: BonusConfiguration[] = [];
  filteredConfigurations: BonusConfiguration[] = [];
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  // Filter
  filterActive = true;
  searchTerm = '';

  // Modal state
  showModal = false;
  modalMode: 'create' | 'edit' | 'view' = 'create';
  selectedConfig: BonusConfiguration | null = null;

  // Form data
  configForm: CreateBonusConfigurationRequest = this.getEmptyForm();

  // Rule being edited in nested form
  editingRuleIndex: number | null = null;
  ruleForm: BonusRuleRequest = this.getEmptyRuleForm();

  // Constants
  positions: string[] = [];
  ruleTypes: { value: BonusRuleType; label: string; description: string }[] = [
    { value: 'OvertimeHours', label: 'Overtime Hours', description: 'Bonus for working beyond scheduled hours' },
    { value: 'UndertimeHours', label: 'Undertime Hours', description: 'Deduction for working less than scheduled hours' },
    { value: 'SnacksPreparation', label: 'Snacks Preparation', description: 'Bonus for preparing extra snacks' },
    { value: 'BadOrders', label: 'Bad Orders', description: 'Deduction based on bad order count' },
    { value: 'GoodRatings', label: 'Good Ratings', description: 'Bonus based on customer good ratings' },
    { value: 'RefundDeduction', label: 'Refund Deduction', description: 'Deduction for orders requiring refund' }
  ];

  calculationTypes: { value: CalculationType; label: string }[] = [
    { value: 'PerUnit', label: 'Per Unit' },
    { value: 'PerHour', label: 'Per Hour' },
    { value: 'Percentage', label: 'Percentage' },
    { value: 'Fixed', label: 'Fixed Amount' }
  ];

  calculationPeriods: CalculationPeriod[] = ['Monthly', 'Quarterly', 'Yearly'];

  ngOnInit(): void {
    this.loadConfigurations();
    this.loadPositions();
  }

  private getEmptyForm(): CreateBonusConfigurationRequest {
    return {
      configurationName: '',
      applicablePositions: [],
      rules: [],
      calculationPeriod: 'Monthly',
      isActive: true
    };
  }

  private getEmptyRuleForm(): BonusRuleRequest {
    return {
      ruleType: 'OvertimeHours',
      isBonus: true,
      calculationType: 'PerHour',
      rateAmount: 0,
      percentageValue: undefined,
      threshold: undefined,
      maxAmount: undefined,
      description: ''
    };
  }

  loadConfigurations(): void {
    this.isLoading = true;
    this.errorMessage = '';

    this.bonusConfigService.getBonusConfigurations().subscribe({
      next: (data) => {
        this.configurations = data;
        this.applyFilters();
        this.isLoading = false;
      },
      error: (error) => {
        this.errorMessage = 'Failed to load bonus configurations: ' + (error.error?.message || error.message);
        this.isLoading = false;
      }
    });
  }

  loadPositions(): void {
    this.staffService.getAllStaff(true).subscribe({
      next: (staff) => {
        // Extract unique positions from active staff
        const positionSet = new Set(staff.map(s => s.position).filter(p => p));
        this.positions = Array.from(positionSet).sort();
      },
      error: (error) => {
        console.error('Failed to load staff positions:', error);
        // Fallback to empty array if staff can't be loaded
        this.positions = [];
      }
    });
  }

  applyFilters(): void {
    this.filteredConfigurations = this.configurations.filter(config => {
      const matchesActive = !this.filterActive || config.isActive;
      const matchesSearch = !this.searchTerm ||
        config.configurationName.toLowerCase().includes(this.searchTerm.toLowerCase()) ||
        config.applicablePositions.some(p => p.toLowerCase().includes(this.searchTerm.toLowerCase()));
      return matchesActive && matchesSearch;
    });
  }

  openCreateModal(): void {
    this.modalMode = 'create';
    this.selectedConfig = null;
    this.configForm = this.getEmptyForm();
    this.editingRuleIndex = null;
    this.showModal = true;
  }

  openEditModal(config: BonusConfiguration): void {
    this.modalMode = 'edit';
    this.selectedConfig = config;
    this.configForm = {
      configurationName: config.configurationName,
      applicablePositions: [...config.applicablePositions],
      rules: config.rules.map(r => ({ ...r })),
      calculationPeriod: config.calculationPeriod,
      isActive: config.isActive
    };
    this.editingRuleIndex = null;
    this.showModal = true;
  }

  openViewModal(config: BonusConfiguration): void {
    this.modalMode = 'view';
    this.selectedConfig = config;
    this.configForm = {
      configurationName: config.configurationName,
      applicablePositions: [...config.applicablePositions],
      rules: config.rules.map(r => ({ ...r })),
      calculationPeriod: config.calculationPeriod,
      isActive: config.isActive
    };
    this.showModal = true;
  }

  closeModal(): void {
    this.showModal = false;
    this.selectedConfig = null;
    this.editingRuleIndex = null;
    this.clearMessages();
  }

  // Rule management
  addRule(): void {
    this.ruleForm = this.getEmptyRuleForm();
    this.editingRuleIndex = -1; // -1 indicates new rule
  }

  editRule(index: number): void {
    this.ruleForm = { ...this.configForm.rules[index] };
    this.editingRuleIndex = index;
  }

  saveRule(): void {
    if (!this.isRuleFormValid()) {
      this.errorMessage = 'Please fill in all required rule fields';
      return;
    }

    if (this.editingRuleIndex === -1) {
      // Adding new rule
      this.configForm.rules.push({ ...this.ruleForm });
    } else if (this.editingRuleIndex !== null) {
      // Updating existing rule
      this.configForm.rules[this.editingRuleIndex] = { ...this.ruleForm };
    }

    this.cancelRuleEdit();
  }

  cancelRuleEdit(): void {
    this.editingRuleIndex = null;
    this.ruleForm = this.getEmptyRuleForm();
  }

  removeRule(index: number): void {
    if (confirm('Are you sure you want to remove this rule?')) {
      this.configForm.rules.splice(index, 1);
    }
  }

  isRuleFormValid(): boolean {
    if (!this.ruleForm.ruleType || this.ruleForm.rateAmount <= 0) {
      return false;
    }
    if (this.ruleForm.calculationType === 'Percentage' && !this.ruleForm.percentageValue) {
      return false;
    }
    return true;
  }

  // Position management
  togglePosition(position: string): void {
    const index = this.configForm.applicablePositions.indexOf(position);
    if (index > -1) {
      this.configForm.applicablePositions.splice(index, 1);
    } else {
      this.configForm.applicablePositions.push(position);
    }
  }

  isPositionSelected(position: string): boolean {
    return this.configForm.applicablePositions.includes(position);
  }

  selectAllPositions(): void {
    this.configForm.applicablePositions = [...this.positions];
  }

  clearAllPositions(): void {
    this.configForm.applicablePositions = [];
  }

  // Form submission
  saveConfiguration(): void {
    if (!this.isFormValid()) {
      this.errorMessage = 'Please fill in all required fields and add at least one rule';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    if (this.modalMode === 'create') {
      this.bonusConfigService.createBonusConfiguration(this.configForm).subscribe({
        next: () => {
          this.successMessage = 'Bonus configuration created successfully!';
          this.loadConfigurations();
          setTimeout(() => this.closeModal(), 1500);
        },
        error: (error) => {
          this.errorMessage = 'Failed to create configuration: ' + (error.error?.message || error.message);
          this.isLoading = false;
        }
      });
    } else if (this.modalMode === 'edit' && this.selectedConfig?.id) {
      const updateRequest: UpdateBonusConfigurationRequest = {
        configurationName: this.configForm.configurationName,
        applicablePositions: this.configForm.applicablePositions,
        rules: this.configForm.rules,
        calculationPeriod: this.configForm.calculationPeriod,
        isActive: this.configForm.isActive
      };

      this.bonusConfigService.updateBonusConfiguration(this.selectedConfig.id, updateRequest).subscribe({
        next: () => {
          this.successMessage = 'Bonus configuration updated successfully!';
          this.loadConfigurations();
          setTimeout(() => this.closeModal(), 1500);
        },
        error: (error) => {
          this.errorMessage = 'Failed to update configuration: ' + (error.error?.message || error.message);
          this.isLoading = false;
        }
      });
    }
  }

  isFormValid(): boolean {
    return this.configForm.configurationName.trim() !== '' &&
           this.configForm.applicablePositions.length > 0 &&
           this.configForm.rules.length > 0;
  }

  deleteConfiguration(config: BonusConfiguration): void {
    if (!config.id) return;

    if (confirm(`Are you sure you want to delete the configuration "${config.configurationName}"?`)) {
      this.isLoading = true;
      this.bonusConfigService.deleteBonusConfiguration(config.id).subscribe({
        next: () => {
          this.successMessage = 'Configuration deleted successfully!';
          this.loadConfigurations();
          setTimeout(() => this.clearMessages(), 3000);
        },
        error: (error) => {
          this.errorMessage = 'Failed to delete configuration: ' + (error.error?.message || error.message);
          this.isLoading = false;
        }
      });
    }
  }

  toggleActiveStatus(config: BonusConfiguration): void {
    if (!config.id) return;

    this.bonusConfigService.toggleActiveStatus(config.id).subscribe({
      next: () => {
        this.successMessage = `Configuration ${config.isActive ? 'deactivated' : 'activated'} successfully!`;
        this.loadConfigurations();
        setTimeout(() => this.clearMessages(), 3000);
      },
      error: (error) => {
        this.errorMessage = 'Failed to toggle status: ' + (error.error?.message || error.message);
      }
    });
  }

  clearMessages(): void {
    this.errorMessage = '';
    this.successMessage = '';
  }

  // Helper methods for display
  getRuleTypeLabel(ruleType: string): string {
    return this.ruleTypes.find(r => r.value === ruleType)?.label || ruleType;
  }

  getCalculationTypeLabel(calcType: string): string {
    return this.calculationTypes.find(c => c.value === calcType)?.label || calcType;
  }

  getRuleDescription(rule: BonusRule): string {
    if (rule.description) return rule.description;

    const typeInfo = this.ruleTypes.find(r => r.value === rule.ruleType);
    const calcType = this.getCalculationTypeLabel(rule.calculationType);
    const amount = rule.calculationType === 'Percentage'
      ? `${rule.percentageValue}%`
      : `₹${rule.rateAmount}`;

    return `${typeInfo?.description || rule.ruleType} - ${calcType}: ${amount}`;
  }
}
